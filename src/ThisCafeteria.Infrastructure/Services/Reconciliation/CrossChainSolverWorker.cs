using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.Infrastructure.Services.Reconciliation;

/// <summary>
/// Standing ERC-7683 solver: watches a configured source chain's resolver for IntentSubmitted
/// events, decodes the full order from the submitting transaction's own calldata (the event alone
/// doesn't carry enough), evaluates the fail-closed <see cref="ISolverPolicyService"/>, and fills
/// approved intents on a configured destination chain from its own key/inventory.
///
/// This is the "off-chain solver" role that contracts/evm/scripts/two-node-crosschain-smoke.ts
/// performed inline as script logic. This worker is the standing-service version of that role.
///
/// Dormant (does nothing, logs once, returns) when <see cref="CrossChainSolverOptions.CanOperate"/>
/// is false — matching the fail-closed posture of every other Phase 4/5 policy in this codebase.
/// </summary>
public sealed class CrossChainSolverWorker(
    IServiceScopeFactory scopeFactory,
    IChainRegistry registry,
    ICrossChainIntentProvider intentProvider,
    ISolverPolicyService policy,
    ICrossChainSolverExecutor executor,
    CrossChainSolverOptions options,
    TimeProvider timeProvider,
    ILogger<CrossChainSolverWorker> logger) : BackgroundService
{
    private const int MaxBlockRangePerScan = 2_000;
    private const int InitialLookbackBlocks = 5_000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    [Function("isResolved", "bool")]
    private sealed class IsResolvedFunction : FunctionMessage
    {
        [Parameter("bytes32", "orderId", 1)] public byte[] OrderId { get; set; } = [];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CanOperate)
        {
            logger.LogInformation(
                "CrossChainSolverWorker is idle: CrossChainSolver is not fully configured (Enabled, chain keys, resolver addresses, and a signer key are all required). This is expected when no solver is deployed.");
            return;
        }

        if (!registry.TryGet(options.SourceChainKey, out var sourceChain) || !sourceChain.Enabled)
        {
            logger.LogWarning("CrossChainSolverWorker is idle: source chain '{ChainKey}' is not a registered, enabled chain.", options.SourceChainKey);
            return;
        }

        if (!registry.TryGet(options.DestinationChainKey, out var destinationChain) || !destinationChain.Enabled)
        {
            logger.LogWarning("CrossChainSolverWorker is idle: destination chain '{ChainKey}' is not a registered, enabled chain.", options.DestinationChainKey);
            return;
        }

        var solverAddress = new Account(options.SolverPrivateKey).Address;
        logger.LogInformation(
            "CrossChainSolverWorker starting: source={SourceChain}/{SourceResolver}, destination={DestChain}/{DestResolver}, solver={Solver}",
            sourceChain.Key, options.SourceResolverAddress, destinationChain.Key, options.DestinationResolverAddress, solverAddress);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(sourceChain, destinationChain, solverAddress, stoppingToken).ConfigureAwait(false);
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Cross-chain solver scan failed; cursor was not advanced");
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
    }

    private async Task ScanOnceAsync(ChainDefinition sourceChain, ChainDefinition destinationChain, string solverAddress, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var latest = await intentProvider.GetLatestBlockNumberAsync(sourceChain, cancellationToken).ConfigureAwait(false);
        var safeHead = latest - Math.Max(0, sourceChain.MinimumConfirmations);

        var checkpoint = await db.CrossChainSolverCheckpoints
            .SingleOrDefaultAsync(cp => cp.SourceChainKey == sourceChain.Key && cp.SourceResolverAddress == options.SourceResolverAddress, cancellationToken)
            .ConfigureAwait(false);

        if (checkpoint is null)
        {
            var startBlock = Math.Max(0, safeHead - InitialLookbackBlocks);
            checkpoint = new CrossChainSolverCheckpoint
            {
                SourceChainKey = sourceChain.Key,
                SourceResolverAddress = options.SourceResolverAddress,
                LastScannedBlock = startBlock - 1
            };
            db.CrossChainSolverCheckpoints.Add(checkpoint);
        }

        var fromBlock = checkpoint.LastScannedBlock + 1;
        if (fromBlock > safeHead) return;
        var toBlock = Math.Min(safeHead, fromBlock + MaxBlockRangePerScan - 1);

        var intents = await intentProvider.GetSubmittedIntentsAsync(sourceChain, options.SourceResolverAddress, fromBlock, toBlock, cancellationToken)
            .ConfigureAwait(false);

        foreach (var intent in intents)
        {
            await EvaluateAndFillAsync(db, sourceChain, destinationChain, solverAddress, intent, cancellationToken).ConfigureAwait(false);
        }

        checkpoint.LastScannedBlock = toBlock;
        checkpoint.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (intents.Count > 0)
        {
            logger.LogInformation("CrossChainSolverWorker: evaluated {Count} intent(s) for {ChainKey} in blocks {From}-{To}", intents.Count, sourceChain.Key, fromBlock, toBlock);
        }
    }

    private async Task EvaluateAndFillAsync(AppDbContext db, ChainDefinition sourceChain, ChainDefinition destinationChain, string solverAddress, SolverIntent intent, CancellationToken cancellationToken)
    {
        var alreadyEvaluated = await db.CrossChainSolverFills.AnyAsync(
            f => f.SourceChainKey == sourceChain.Key && f.SourceResolverAddress == options.SourceResolverAddress && f.OrderId == intent.OrderId,
            cancellationToken).ConfigureAwait(false);
        if (alreadyEvaluated) return;

        var decision = policy.Evaluate(intent, solverAddress, timeProvider.GetUtcNow());
        if (!decision.Approved)
        {
            logger.LogInformation("Solver declined intent {OrderId}: {Reason} - {Detail}", intent.OrderId, decision.Reason, decision.Detail);
            db.CrossChainSolverFills.Add(new CrossChainSolverFill
            {
                SourceChainKey = sourceChain.Key,
                SourceResolverAddress = options.SourceResolverAddress,
                OrderId = intent.OrderId,
                SubmitTransactionHash = intent.SubmitTransactionHash,
                Filled = false,
                DenialReason = $"{decision.Reason}: {decision.Detail}"
            });
            return;
        }

        // Defensive idempotency: even though CrossChainSolverFills should prevent this worker from
        // ever attempting the same orderId twice, check the destination contract's own resolution
        // state before spending gas on a fillIntent call the contract itself will refuse
        // ("Already resolved") — e.g. after a DB write failure that followed a successful fill.
        var alreadyResolvedOnChain = await IsResolvedOnDestinationAsync(destinationChain, intent.OrderId, cancellationToken).ConfigureAwait(false);
        if (alreadyResolvedOnChain)
        {
            logger.LogWarning("Intent {OrderId} was already resolved on the destination chain before this worker recorded it - recording as filled without resubmitting.", intent.OrderId);
            db.CrossChainSolverFills.Add(new CrossChainSolverFill
            {
                SourceChainKey = sourceChain.Key,
                SourceResolverAddress = options.SourceResolverAddress,
                OrderId = intent.OrderId,
                SubmitTransactionHash = intent.SubmitTransactionHash,
                Filled = true,
                FillTransactionHash = null
            });
            return;
        }

        var fillTxHash = await executor.FillIntentAsync(destinationChain, options.DestinationResolverAddress, options.SolverPrivateKey, intent, decision.AmountOut, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("Solver filled intent {OrderId} for {AmountOut} on {ChainKey}, tx {TxHash}", intent.OrderId, decision.AmountOut, destinationChain.Key, fillTxHash);

        db.CrossChainSolverFills.Add(new CrossChainSolverFill
        {
            SourceChainKey = sourceChain.Key,
            SourceResolverAddress = options.SourceResolverAddress,
            OrderId = intent.OrderId,
            SubmitTransactionHash = intent.SubmitTransactionHash,
            Filled = true,
            FillTransactionHash = fillTxHash
        });
    }

    private async Task<bool> IsResolvedOnDestinationAsync(ChainDefinition destinationChain, string orderIdHex, CancellationToken cancellationToken)
    {
        var web3 = new Web3(destinationChain.EffectiveServerRpcUrl);
        var handler = web3.Eth.GetContractQueryHandler<IsResolvedFunction>();
        return await handler.QueryAsync<bool>(options.DestinationResolverAddress, new IsResolvedFunction
        {
            OrderId = Convert.FromHexString(orderIdHex[2..])
        }).ConfigureAwait(false);
    }
}
