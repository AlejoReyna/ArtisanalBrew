using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services.Blockchain;

public sealed class StakingLedgerService(
    ICoffeeWeb3Service web3Service,
    IStakingLedgerRepository ledger) : IStakingLedgerService
{
    /// <summary>
    /// The only network these endpoints have ever recorded against. Kept as a constant here
    /// rather than derived from configuration so the ledger rows stay identical to those the
    /// controller wrote before this logic moved.
    /// </summary>
    private const string LegacyChainKey = "ethereum-sepolia";
    private const string LegacyFamily = "Evm";

    public async Task<StakingRecordResult> RecordAsync(
        BlockchainNetworkOptions chain,
        string walletAddress,
        string transactionHash,
        StakingTransactionType transactionType,
        decimal? expectedAmount,
        CancellationToken cancellationToken = default)
    {
        // Checked by hash alone, across every chain - broader than the ledger's own
        // (chain, transaction, operation) identity, and intentionally so: one transaction hash
        // may only ever be recorded once through this endpoint.
        if (await ledger.ExistsByTransactionHashAsync(transactionHash, cancellationToken).ConfigureAwait(false))
        {
            return StakingRecordResult.AlreadyRecorded;
        }

        var verification = await web3Service
            .VerifyStakingTransactionAsync(transactionHash, walletAddress, expectedAmount, transactionType, cancellationToken)
            .ConfigureAwait(false);

        if (verification.Status == TransactionVerificationStatus.PendingConfirmations)
        {
            return StakingRecordResult.PendingConfirmations(verification);
        }

        if (!verification.Verified)
        {
            return StakingRecordResult.VerificationFailed;
        }

        var entry = BuildEntry(chain, walletAddress, transactionHash, transactionType, verification.Amount);
        var write = await ledger.AddIfAbsentAsync(entry, cancellationToken).ConfigureAwait(false);

        return write.Added
            ? StakingRecordResult.Recorded(entry)
            : StakingRecordResult.AlreadyRecorded;
    }

    public async Task<IReadOnlyList<StakingLedgerEntry>> GetRecentActivityAsync(
        string walletAddress,
        int take = 8,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            return [];
        }

        return await ledger.ListByWalletAsync(walletAddress, take, cancellationToken).ConfigureAwait(false);
    }

    private static StakingLedgerEntry BuildEntry(
        BlockchainNetworkOptions chain,
        string walletAddress,
        string transactionHash,
        StakingTransactionType transactionType,
        decimal amount)
    {
        var entry = StakingLedgerEntry.Create(LegacyChainKey, transactionHash, 0, entry =>
        {
            entry.WalletAddress = walletAddress;
            entry.Family = LegacyFamily;
            entry.ActionType = ToActionType(transactionType);
            entry.Amount = amount;
            entry.ChainId = chain.ChainId;
            entry.NetworkName = chain.NetworkName;
            entry.StakingPoolContract = chain.StakingPoolContract;
            entry.VaultOrProgramIdentifier = chain.StakingPoolContract;
            entry.Verified = true;
            entry.VerificationState = "verified";
            entry.ExplorerUrl = BuildExplorerTransactionUrl(chain, transactionHash);
            entry.RecordedAtUtc = DateTime.UtcNow;
        });

        // A claim pays out reward tokens; a stake or unstake moves principal. The two populate
        // different amount columns, and the contract recorded against differs to match.
        if (transactionType == StakingTransactionType.Claim)
        {
            entry.RewardAmount = amount;
            entry.RawRewardAmount = StakingAmountRules.ToRawAmount(amount);
            entry.PaymentTokenContract = chain.CoffeeCoinContract;
            entry.RewardIdentifier = chain.CoffeeCoinContract;
        }
        else
        {
            entry.AssetAmount = amount;
            entry.ShareAmount = amount;
            entry.RawAssetAmount = StakingAmountRules.ToRawAmount(amount);
            entry.RawShareAmount = StakingAmountRules.ToRawAmount(amount);
            entry.PaymentTokenContract = chain.EffectivePaymentTokenContract;
            entry.AssetIdentifier = chain.EffectivePaymentTokenContract;
        }

        return entry;
    }

    private static string ToActionType(StakingTransactionType transactionType) => transactionType switch
    {
        StakingTransactionType.Stake => "stake",
        StakingTransactionType.Unstake => "unstake",
        _ => "claim"
    };

    private static string BuildExplorerTransactionUrl(BlockchainNetworkOptions chain, string transactionHash)
    {
        var explorer = chain.ExplorerUrl?.Trim();
        return string.IsNullOrWhiteSpace(explorer)
            ? string.Empty
            : $"{explorer.TrimEnd('/')}/tx/{transactionHash}";
    }
}
