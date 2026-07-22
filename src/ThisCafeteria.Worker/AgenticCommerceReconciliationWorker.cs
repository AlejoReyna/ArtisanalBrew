using System.Numerics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.Worker;

/// <summary>
/// Reads escrow events from configured EVM AgenticCommerceEscrow deployments
/// and reconciles them idempotently into AgenticJobProjection rows.
/// 
/// The worker remains dormant when:
///   - No chain with AgenticCommerce capability is configured.
///   - No AgenticEscrow deployment address is set.
///
/// Design mirrors ChainReconciliationSupervisor:
///   - Block cursor advances only in the same DB transaction as projections.
///   - Reorg-safe confirmation depth from chain definition.
///   - Idempotent via (ChainKey, OnChainJobId) compound uniqueness.
///   - Structured Serilog logging throughout.
/// </summary>
public sealed class AgenticCommerceReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IChainRegistry registry,
    IEscrowEventProvider eventProvider,
    IAgenticCommerceReconciliationApplicator applicator,
    ILogger<AgenticCommerceReconciliationWorker> logger) : BackgroundService
{
    private const int MaxBlockRangePerScan = 2_000;
    private const int InitialLookbackBlocks = 5_000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var deployments = registry.All
            .Where(chain => chain.Enabled && chain.Family == ChainFamily.Evm && chain.Capabilities.AgenticCommerce)
            .Where(chain => !string.IsNullOrWhiteSpace(chain.Deployment.AgenticEscrow))
            .ToArray();

        if (deployments.Length == 0)
        {
            logger.LogInformation(
                "AgenticCommerceReconciliationWorker is idle: no enabled chains with AgenticCommerce capability and an AgenticEscrow deployment address. " +
                "This is expected when the escrow contract has not been deployed.");
            return;
        }

        logger.LogInformation("AgenticCommerceReconciliationWorker starting for {Count} chain(s): {Chains}",
            deployments.Length, string.Join(", ", deployments.Select(c => c.Key)));

        await Task.WhenAll(deployments.Select(chain => RunChainAsync(chain, stoppingToken))).ConfigureAwait(false);
    }

    private async Task RunChainAsync(ChainDefinition chain, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileOnceAsync(chain, stoppingToken).ConfigureAwait(false);
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Escrow reconciliation failed for chain {ChainKey}; cursor was not advanced",
                    chain.Key);
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
    }

    public async Task ReconcileOnceAsync(ChainDefinition chain, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var latest = await eventProvider.GetLatestBlockNumberAsync(chain, cancellationToken).ConfigureAwait(false);
        var safeHead = latest - Math.Max(0, chain.MinimumConfirmations);

        var escrowAddress = chain.Deployment.AgenticEscrow;
        var checkpoint = await db.AgenticCommerceCheckpoints
            .SingleOrDefaultAsync(
                cp => cp.ChainKey == chain.Key && cp.EscrowAddress == escrowAddress,
                cancellationToken)
            .ConfigureAwait(false);

        if (checkpoint is null)
        {
            var startBlock = chain.Deployment.StartBlockOrSlot > 0
                ? chain.Deployment.StartBlockOrSlot
                : Math.Max(0, safeHead - InitialLookbackBlocks);

            checkpoint = new AgenticCommerceReconciliationCheckpoint
            {
                ChainKey = chain.Key,
                EscrowAddress = escrowAddress,
                LastScannedBlock = startBlock - 1
            };
            db.AgenticCommerceCheckpoints.Add(checkpoint);
        }

        var fromBlock = checkpoint.LastScannedBlock + 1;
        logger.LogInformation("AgenticCommerceReconciliationWorker: chain={Chain}, latest={Latest}, safeHead={SafeHead}, fromBlock={FromBlock}", chain.Key, latest, safeHead, fromBlock);

        if (fromBlock > safeHead) return;
        var toBlock = Math.Min(safeHead, fromBlock + MaxBlockRangePerScan - 1);
        logger.LogInformation("AgenticCommerceReconciliationWorker: scanning {FromBlock}-{ToBlock}", fromBlock, toBlock);

        var events = await eventProvider.DecodeEventsAsync(chain, escrowAddress, fromBlock, toBlock, cancellationToken)
            .ConfigureAwait(false);

        var registryAddress = chain.Deployment.ERC8004Registry;
        var registryEvents = string.IsNullOrWhiteSpace(registryAddress)
            ? new List<RegistryEvent>()
            : await eventProvider.DecodeRegistryEventsAsync(chain, registryAddress, fromBlock, toBlock, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("AgenticCommerceReconciliationWorker: found {Count} registry events in {FromBlock}-{ToBlock}", registryEvents.Count, fromBlock, toBlock);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var evt in events.OrderBy(e => e.BlockNumber).ThenBy(e => e.LogIndex))
        {
            await applicator.ApplyEventAsync(db, chain, escrowAddress, evt, cancellationToken).ConfigureAwait(false);
        }

        foreach (var evt in registryEvents.OrderBy(e => e.BlockNumber).ThenBy(e => e.LogIndex))
        {
            await applicator.ApplyRegistryEventAsync(db, chain, registryAddress, evt, cancellationToken).ConfigureAwait(false);
        }

        checkpoint.LastScannedBlock = toBlock;
        checkpoint.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (events.Count > 0 || registryEvents.Count > 0)
        {
            logger.LogInformation(
                "Reconciled {Count} escrow events and {RegCount} registry events for {ChainKey} in blocks {From}-{To}",
                events.Count, registryEvents.Count, chain.Key, fromBlock, toBlock);
        }
        else
        {
            logger.LogDebug(
                "No events for {ChainKey} in blocks {From}-{To}",
                chain.Key, fromBlock, toBlock);
        }
    }

    // --- Internal types ---



    // --- Nethereum event DTOs (match AgenticCommerceEscrow.sol events) ---

    [Event("JobCreated")]
    public sealed class JobCreatedEventDTO : IEventDTO
    {
        [Parameter("uint256", "jobId", 1, true)]
        public BigInteger JobId { get; set; }
        [Parameter("address", "client", 2, true)]
        public string Client { get; set; } = string.Empty;
        [Parameter("address", "provider", 3, true)]
        public string Provider { get; set; } = string.Empty;
        [Parameter("address", "evaluator", 4, false)]
        public string Evaluator { get; set; } = string.Empty;
        [Parameter("uint256", "expiredAt", 5, false)]
        public BigInteger ExpiredAt { get; set; }
    }

    [Event("JobFunded")]
    public sealed class JobFundedEventDTO : IEventDTO
    {
        [Parameter("uint256", "jobId", 1, true)]
        public BigInteger JobId { get; set; }
        [Parameter("address", "client", 2, true)]
        public string Client { get; set; } = string.Empty;
        [Parameter("uint256", "amount", 3, false)]
        public BigInteger Amount { get; set; }
    }

    [Event("JobSubmitted")]
    public sealed class JobSubmittedEventDTO : IEventDTO
    {
        [Parameter("uint256", "jobId", 1, true)]
        public BigInteger JobId { get; set; }
        [Parameter("address", "provider", 2, true)]
        public string Provider { get; set; } = string.Empty;
        [Parameter("bytes32", "deliverable", 3, false)]
        public byte[] Deliverable { get; set; } = Array.Empty<byte>();
    }

    [Event("JobCompleted")]
    public sealed class JobCompletedEventDTO : IEventDTO
    {
        [Parameter("uint256", "jobId", 1, true)]
        public BigInteger JobId { get; set; }
        [Parameter("address", "evaluator", 2, true)]
        public string Evaluator { get; set; } = string.Empty;
        [Parameter("bytes32", "reason", 3, false)]
        public byte[] Reason { get; set; } = Array.Empty<byte>();
    }

    [Event("JobRejected")]
    public sealed class JobRejectedEventDTO : IEventDTO
    {
        [Parameter("uint256", "jobId", 1, true)]
        public BigInteger JobId { get; set; }
        [Parameter("address", "rejector", 2, true)]
        public string Rejector { get; set; } = string.Empty;
        [Parameter("bytes32", "reason", 3, false)]
        public byte[] Reason { get; set; } = Array.Empty<byte>();
    }

    [Event("JobExpired")]
    public sealed class JobExpiredEventDTO : IEventDTO
    {
        [Parameter("uint256", "jobId", 1, true)]
        public BigInteger JobId { get; set; }
    }

    [Event("ProviderSet")]
    public sealed class ProviderSetEventDTO : IEventDTO
    {
        [Parameter("uint256", "jobId", 1, true)]
        public BigInteger JobId { get; set; }
        [Parameter("address", "provider", 2, true)]
        public string Provider { get; set; } = string.Empty;
    }

    [Event("BudgetSet")]
    public sealed class BudgetSetEventDTO : IEventDTO
    {
        [Parameter("uint256", "jobId", 1, true)]
        public BigInteger JobId { get; set; }
        [Parameter("uint256", "amount", 2, false)]
        public BigInteger Amount { get; set; }
    }

    [Event("PaymentReleased")]
    public sealed class PaymentReleasedEventDTO : IEventDTO
    {
        [Parameter("uint256", "jobId", 1, true)]
        public BigInteger JobId { get; set; }
        [Parameter("address", "provider", 2, true)]
        public string Provider { get; set; } = string.Empty;
        [Parameter("uint256", "amount", 3, false)]
        public BigInteger Amount { get; set; }
    }

    [Event("Refunded")]
    public sealed class RefundedEventDTO : IEventDTO
    {
        [Parameter("uint256", "jobId", 1, true)]
        public BigInteger JobId { get; set; }
        [Parameter("address", "client", 2, true)]
        public string Client { get; set; } = string.Empty;
        [Parameter("uint256", "amount", 3, false)]
        public BigInteger Amount { get; set; }
    }

    [Event("AgentRegistered")]
    public sealed class AgentRegisteredEventDTO : IEventDTO
    {
        [Parameter("uint256", "agentId", 1, true)]
        public BigInteger AgentId { get; set; }
        [Parameter("address", "owner", 2, true)]
        public string Owner { get; set; } = string.Empty;
        [Parameter("string", "metadataURI", 3, false)]
        public string MetadataURI { get; set; } = string.Empty;
    }
}
