using System.Numerics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.Infrastructure.Services.Reconciliation;

/// <summary>
/// Reconciles configured ERC-4626 vault events. A block cursor is moved only in
/// the same database transaction as every decoded projection in that range.
/// </summary>
public sealed class ChainReconciliationSupervisor(
    IServiceScopeFactory scopeFactory,
    IChainRegistry registry,
    ILogger<ChainReconciliationSupervisor> logger) : BackgroundService
{
    private const int MaxBlockRangePerScan = 2_000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var deployments = registry.All
            .Where(chain => chain.Enabled && chain.Family == ChainFamily.Evm && chain.Capabilities.LiquidStaking)
            .Where(chain => !string.IsNullOrWhiteSpace(chain.Deployment.LiquidVault))
            .ToArray();

        if (deployments.Length == 0)
        {
            logger.LogInformation("Multichain reconciliation supervisor is idle; no enabled liquid-vault manifests are loaded.");
            return;
        }

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
                logger.LogError(exception, "Reconciliation failed for chain {ChainKey}; cursor was not advanced", chain.Key);
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
        }
    }

    private async Task ReconcileOnceAsync(ChainDefinition chain, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var web3 = new Web3(chain.EffectiveServerRpcUrl);
        var latest = (long)(await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync().ConfigureAwait(false)).Value;
        var safeHead = latest - Math.Max(0, chain.MinimumConfirmations);
        if (safeHead < chain.Deployment.StartBlockOrSlot) return;

        var source = chain.Deployment.LiquidVault;
        var checkpoint = await db.StakingReconciliationCheckpoints
            .SingleOrDefaultAsync(item => item.ChainKey == chain.Key && item.SourceIdentifier == source, cancellationToken)
            .ConfigureAwait(false);

        if (checkpoint is null)
        {
            checkpoint = new StakingReconciliationCheckpoint
            {
                ChainKey = chain.Key,
                Family = chain.FamilyName,
                SourceIdentifier = source,
                CursorType = "block",
                StakingPoolContract = source,
                LastScannedBlock = chain.Deployment.StartBlockOrSlot - 1
            };
            db.StakingReconciliationCheckpoints.Add(checkpoint);
        }

        var fromBlock = Math.Max(checkpoint.LastScannedBlock + 1, chain.Deployment.StartBlockOrSlot);
        if (fromBlock > safeHead) return;
        var toBlock = Math.Min(safeHead, fromBlock + MaxBlockRangePerScan - 1);

        var projections = await DecodeRangeAsync(web3, chain, fromBlock, toBlock, cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var projection in projections.OrderBy(item => item.BlockOrSlot).ThenBy(item => item.OperationIndex))
        {
            var exists = await db.StakingLedgerEntries.AnyAsync(item =>
                item.ChainKey == projection.ChainKey &&
                item.TransactionHash == projection.TransactionHash &&
                item.OperationIndex == projection.OperationIndex, cancellationToken).ConfigureAwait(false);
            if (!exists) db.StakingLedgerEntries.Add(projection);
        }

        checkpoint.LastScannedBlock = toBlock;
        checkpoint.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        logger.LogDebug("Reconciled {Count} vault events for {ChainKey} in blocks {From}-{To}", projections.Count, chain.Key, fromBlock, toBlock);
    }

    private static async Task<List<StakingLedgerEntry>> DecodeRangeAsync(Web3 web3, ChainDefinition chain, long fromBlock, long toBlock, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = chain.Deployment.LiquidVault;
        var from = new BlockParameter((ulong)fromBlock);
        var to = new BlockParameter((ulong)toBlock);
        var result = new List<StakingLedgerEntry>();

        var deposits = web3.Eth.GetEvent<DepositEventDTO>(source);
        foreach (var item in await deposits.GetAllChangesAsync(deposits.CreateFilterInput(from, to)).ConfigureAwait(false))
            result.Add(ToEntry(chain, item.Log, item.Event.Receiver, "deposit", item.Event.Assets, item.Event.Shares, 0));

        var withdrawals = web3.Eth.GetEvent<WithdrawEventDTO>(source);
        foreach (var item in await withdrawals.GetAllChangesAsync(withdrawals.CreateFilterInput(from, to)).ConfigureAwait(false))
            result.Add(ToEntry(chain, item.Log, item.Event.Owner, "redeem", item.Event.Assets, item.Event.Shares, 0));

        var claims = web3.Eth.GetEvent<RewardPaidEventDTO>(source);
        foreach (var item in await claims.GetAllChangesAsync(claims.CreateFilterInput(from, to)).ConfigureAwait(false))
            result.Add(ToEntry(chain, item.Log, item.Event.Account, "claim", 0, 0, item.Event.Reward));

        var funded = web3.Eth.GetEvent<RewardAddedEventDTO>(source);
        foreach (var item in await funded.GetAllChangesAsync(funded.CreateFilterInput(from, to)).ConfigureAwait(false))
            result.Add(ToEntry(chain, item.Log, "0x0000000000000000000000000000000000000000", "reward_funding", item.Event.Amount, 0, 0));

        return result;
    }

    private static StakingLedgerEntry ToEntry(ChainDefinition chain, FilterLog log, string wallet, string action, BigInteger assets, BigInteger shares, BigInteger rewards) =>
        StakingLedgerEntry.Create(
            chain.Key,
            log.TransactionHash?.ToLowerInvariant() ?? string.Empty,
            checked((int)(log.LogIndex?.Value ?? 0)),
            entry =>
            {
                entry.WalletAddress = wallet;
                entry.Family = chain.FamilyName;
                entry.ActionType = action;
                entry.Amount = FromWei(assets != 0 ? assets : rewards);
                entry.AssetAmount = FromWei(assets);
                entry.ShareAmount = FromWei(shares);
                entry.RewardAmount = FromWei(rewards);
                entry.RawAssetAmount = assets.ToString();
                entry.RawShareAmount = shares.ToString();
                entry.RawRewardAmount = rewards.ToString();
                entry.ChainId = chain.EvmChainId ?? 0;
                entry.NetworkName = chain.DisplayName;
                entry.PaymentTokenContract = chain.Deployment.Cafe;
                entry.StakingPoolContract = chain.Deployment.LiquidVault;
                entry.AssetIdentifier = chain.Deployment.Cafe;
                entry.ReceiptIdentifier = chain.Deployment.StCafe;
                entry.RewardIdentifier = chain.Deployment.Coffee;
                entry.VaultOrProgramIdentifier = chain.Deployment.LiquidVault;
                entry.BlockOrSlot = (long)(log.BlockNumber?.Value ?? 0);
                entry.VerificationState = "reconciled";
                entry.Verified = true;
                entry.RecordedAtUtc = DateTime.UtcNow;
                entry.OccurredAtUtc = DateTime.UtcNow;
                entry.ExplorerUrl = string.Format(chain.ExplorerTransactionTemplate, log.TransactionHash);
            });

    private static decimal FromWei(BigInteger value) => Web3.Convert.FromWei(value, 18);

    [Event("Deposit")]
    private sealed class DepositEventDTO : IEventDTO
    {
        [Parameter("address", "caller", 1, true)] public string Caller { get; set; } = string.Empty;
        [Parameter("address", "receiver", 2, true)] public string Receiver { get; set; } = string.Empty;
        [Parameter("uint256", "assets", 3, false)] public BigInteger Assets { get; set; }
        [Parameter("uint256", "shares", 4, false)] public BigInteger Shares { get; set; }
    }

    [Event("Withdraw")]
    private sealed class WithdrawEventDTO : IEventDTO
    {
        [Parameter("address", "caller", 1, true)] public string Caller { get; set; } = string.Empty;
        [Parameter("address", "receiver", 2, true)] public string Receiver { get; set; } = string.Empty;
        [Parameter("address", "owner", 3, true)] public string Owner { get; set; } = string.Empty;
        [Parameter("uint256", "assets", 4, false)] public BigInteger Assets { get; set; }
        [Parameter("uint256", "shares", 5, false)] public BigInteger Shares { get; set; }
    }

    [Event("RewardPaid")]
    private sealed class RewardPaidEventDTO : IEventDTO
    {
        [Parameter("address", "account", 1, true)] public string Account { get; set; } = string.Empty;
        [Parameter("uint256", "reward", 2, true)] public BigInteger Reward { get; set; }
    }

    [Event("RewardAdded")]
    private sealed class RewardAddedEventDTO : IEventDTO
    {
        [Parameter("uint256", "amount", 1, true)] public BigInteger Amount { get; set; }
        [Parameter("uint256", "duration", 2, true)] public BigInteger Duration { get; set; }
        [Parameter("uint256", "rewardRate", 3, false)] public BigInteger RewardRate { get; set; }
        [Parameter("uint256", "periodFinish", 4, false)] public BigInteger PeriodFinish { get; set; }
    }
}
