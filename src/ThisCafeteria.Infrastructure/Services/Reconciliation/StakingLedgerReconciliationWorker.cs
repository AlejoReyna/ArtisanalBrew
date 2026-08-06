using System.Numerics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

namespace ThisCafeteria.Infrastructure.Services.Reconciliation;

public sealed class StakingLedgerReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<BlockchainNetworkOptions> chainOptions,
    ILogger<StakingLedgerReconciliationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private const int MaxBlockRangePerScan = 2_000;
    private const int InitialLookbackBlocks = 5_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var chain = chainOptions.Value;
        if (!IsValidContract(chain.StakingPoolContract))
        {
            logger.LogWarning("Staking ledger reconciliation worker idle; Blockchain:Network:StakingPoolContract is not configured.");
            return;
        }

        var web3 = new Web3(chain.RpcUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileOnceAsync(web3, chain, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Staking ledger reconciliation pass failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on graceful shutdown.
            }
        }
    }

    private async Task ReconcileOnceAsync(
        Web3 web3,
        BlockchainNetworkOptions chain,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var latestBlock = (long)(await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync().ConfigureAwait(false)).Value;
        var safeLatestBlock = latestBlock - chain.MinimumConfirmations;

        const string chainKey = "ethereum-sepolia";
        var checkpoint = await dbContext.StakingReconciliationCheckpoints
            .FirstOrDefaultAsync(c => c.ChainKey == chainKey && c.SourceIdentifier == chain.StakingPoolContract, cancellationToken)
            .ConfigureAwait(false);

        long fromBlock;
        if (checkpoint is null)
        {
            fromBlock = Math.Max(0, safeLatestBlock - InitialLookbackBlocks);
            checkpoint = new StakingReconciliationCheckpoint
            {
                StakingPoolContract = chain.StakingPoolContract,
                ChainKey = chainKey,
                Family = "Evm",
                SourceIdentifier = chain.StakingPoolContract,
                CursorType = "block",
                LastScannedBlock = fromBlock - 1
            };
            dbContext.StakingReconciliationCheckpoints.Add(checkpoint);
        }
        else
        {
            fromBlock = checkpoint.LastScannedBlock + 1;
        }

        if (fromBlock > safeLatestBlock)
        {
            return;
        }

        var toBlock = Math.Min(safeLatestBlock, fromBlock + MaxBlockRangePerScan - 1);

        var blockTimestamps = new Dictionary<ulong, DateTime>();
        var insertedCount = 0;
        await using var projectionTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (IsValidContract(chain.EffectivePaymentTokenContract))
        {
            var paymentTransfers = await GetTransferLogsAsync(
                web3, chain.EffectivePaymentTokenContract, fromBlock, toBlock, cancellationToken).ConfigureAwait(false);

            foreach (var transfer in paymentTransfers)
            {
                if (AddressMatches(transfer.Event.To, chain.StakingPoolContract))
                {
                    insertedCount += await TryInsertLedgerEntryAsync(
                        dbContext,
                        chain,
                        web3,
                        blockTimestamps,
                        transfer,
                        wallet: transfer.Event.From,
                        actionType: "stake",
                        tokenContract: chain.EffectivePaymentTokenContract,
                        cancellationToken).ConfigureAwait(false);
                }
                else if (AddressMatches(transfer.Event.From, chain.StakingPoolContract))
                {
                    insertedCount += await TryInsertLedgerEntryAsync(
                        dbContext,
                        chain,
                        web3,
                        blockTimestamps,
                        transfer,
                        wallet: transfer.Event.To,
                        actionType: "unstake",
                        tokenContract: chain.EffectivePaymentTokenContract,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        if (IsValidContract(chain.CoffeeCoinContract))
        {
            var claimTransfers = await GetTransferLogsAsync(
                web3, chain.CoffeeCoinContract, fromBlock, toBlock, cancellationToken).ConfigureAwait(false);

            foreach (var transfer in claimTransfers)
            {
                if (AddressMatches(transfer.Event.From, chain.StakingPoolContract))
                {
                    insertedCount += await TryInsertLedgerEntryAsync(
                        dbContext,
                        chain,
                        web3,
                        blockTimestamps,
                        transfer,
                        wallet: transfer.Event.To,
                        actionType: "claim",
                        tokenContract: chain.CoffeeCoinContract,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        checkpoint.LastScannedBlock = toBlock;
        checkpoint.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await projectionTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (insertedCount > 0)
        {
            logger.LogInformation(
                "Staking ledger reconciliation inserted {InsertedCount} missing entries scanning blocks {FromBlock}-{ToBlock}.",
                insertedCount,
                fromBlock,
                toBlock);
        }
    }

    private static async Task<List<EventLog<TransferEventDTO>>> GetTransferLogsAsync(
        Web3 web3,
        string tokenContract,
        long fromBlock,
        long toBlock,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var transferEvent = web3.Eth.GetEvent<TransferEventDTO>(tokenContract);
        var filter = transferEvent.CreateFilterInput(
            new BlockParameter((ulong)fromBlock),
            new BlockParameter((ulong)toBlock));

        return await transferEvent.GetAllChangesAsync(filter).ConfigureAwait(false);
    }

    private async Task<int> TryInsertLedgerEntryAsync(
        AppDbContext dbContext,
        BlockchainNetworkOptions chain,
        Web3 web3,
        Dictionary<ulong, DateTime> blockTimestamps,
        EventLog<TransferEventDTO> transfer,
        string wallet,
        string actionType,
        string tokenContract,
        CancellationToken cancellationToken)
    {
        var transactionHash = transfer.Log.TransactionHash;
        if (string.IsNullOrWhiteSpace(transactionHash))
        {
            return 0;
        }

        var operationIndex = checked((int)(transfer.Log.LogIndex?.Value ?? 0));
        var alreadyRecorded = await dbContext.StakingLedgerEntries
            .AnyAsync(entry => entry.ChainKey == "ethereum-sepolia" && entry.TransactionHash == transactionHash.ToLowerInvariant() && entry.OperationIndex == operationIndex, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyRecorded)
        {
            return 0;
        }

        var recordedAtUtc = await GetBlockTimestampAsync(web3, blockTimestamps, transfer.Log.BlockNumber, cancellationToken)
            .ConfigureAwait(false);

        var entry = StakingLedgerEntry.Create("ethereum-sepolia", transactionHash.ToLowerInvariant(), operationIndex, entry =>
        {
            entry.WalletAddress = wallet;
            entry.Family = "Evm";
            entry.ActionType = actionType;
            entry.Amount = Web3.Convert.FromWei(transfer.Event.Value);
            entry.AssetAmount = Web3.Convert.FromWei(transfer.Event.Value);
            entry.RawAssetAmount = transfer.Event.Value.ToString();
            entry.ChainId = chain.ChainId;
            entry.NetworkName = chain.NetworkName;
            entry.PaymentTokenContract = tokenContract;
            entry.StakingPoolContract = chain.StakingPoolContract;
            entry.ExplorerUrl = BuildExplorerTransactionUrl(chain, transactionHash);
            entry.RecordedAtUtc = recordedAtUtc;
        });

        dbContext.StakingLedgerEntries.Add(entry);

        return 1;
    }

    private static async Task<DateTime> GetBlockTimestampAsync(
        Web3 web3,
        Dictionary<ulong, DateTime> blockTimestamps,
        HexBigInteger? blockNumber,
        CancellationToken cancellationToken)
    {
        if (blockNumber is null)
        {
            return DateTime.UtcNow;
        }

        var blockNumberValue = (ulong)blockNumber.Value;
        if (blockTimestamps.TryGetValue(blockNumberValue, out var cachedTimestamp))
        {
            return cachedTimestamp;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var block = await web3.Eth.Blocks.GetBlockWithTransactionsHashesByNumber
            .SendRequestAsync(new BlockParameter(blockNumberValue))
            .ConfigureAwait(false);

        var timestamp = DateTimeOffset.FromUnixTimeSeconds((long)block.Timestamp.Value).UtcDateTime;
        blockTimestamps[blockNumberValue] = timestamp;
        return timestamp;
    }

    private static string BuildExplorerTransactionUrl(BlockchainNetworkOptions chain, string transactionHash)
    {
        var explorer = chain.ExplorerUrl?.Trim();
        return string.IsNullOrWhiteSpace(explorer)
            ? string.Empty
            : $"{explorer.TrimEnd('/')}/tx/{transactionHash}";
    }

    private static bool IsValidContract(string? address) =>
        !string.IsNullOrWhiteSpace(address) &&
        AddressUtil.Current.IsValidEthereumAddressHexFormat(address) &&
        !address.Equals("0x0000000000000000000000000000000000000000", StringComparison.OrdinalIgnoreCase);

    private static bool AddressMatches(string? actual, string expected) =>
        !string.IsNullOrWhiteSpace(actual) &&
        actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    [Event("Transfer")]
    private sealed class TransferEventDTO : IEventDTO
    {
        [Parameter("address", "from", 1, true)]
        public string From { get; set; } = string.Empty;

        [Parameter("address", "to", 2, true)]
        public string To { get; set; } = string.Empty;

        [Parameter("uint256", "value", 3, false)]
        public BigInteger Value { get; set; }
    }
}
