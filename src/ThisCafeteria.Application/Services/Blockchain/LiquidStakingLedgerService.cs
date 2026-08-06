using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services.Blockchain;

public sealed class LiquidStakingLedgerService(
    ILiquidStakingGateway gateway,
    IStakingLedgerRepository ledger) : ILiquidStakingLedgerService
{
    public async Task<LiquidStakingRecordResult> RecordAsync(
        ChainDefinition chain,
        string walletIdentifier,
        string transactionId,
        LiquidStakingOperation operation,
        decimal? expectedAmount,
        CancellationToken cancellationToken = default)
    {
        var verification = await gateway
            .VerifyAsync(chain.Key, walletIdentifier, transactionId, operation, expectedAmount, cancellationToken)
            .ConfigureAwait(false);

        if (verification.Status == TransactionVerificationStatus.PendingConfirmations)
        {
            return LiquidStakingRecordResult.PendingConfirmations;
        }

        if (!verification.Verified)
        {
            return LiquidStakingRecordResult.VerificationFailed(verification.Error);
        }

        var existing = await ledger
            .FindByOperationAsync(chain.Key, transactionId, verification.OperationIndex, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return LiquidStakingRecordResult.AlreadyRecorded(existing);
        }

        var entry = BuildEntry(chain, walletIdentifier, transactionId, operation, verification);
        var write = await ledger.AddIfAbsentAsync(entry, cancellationToken).ConfigureAwait(false);

        if (write.Added)
        {
            return LiquidStakingRecordResult.Recorded(entry);
        }

        return write.Existing is not null
            ? LiquidStakingRecordResult.AlreadyRecorded(write.Existing)
            : LiquidStakingRecordResult.Conflict;
    }

    private static StakingLedgerEntry BuildEntry(
        ChainDefinition chain,
        string walletIdentifier,
        string transactionId,
        LiquidStakingOperation operation,
        LiquidTransactionVerificationResult verification)
    {
        // Solana identifies the pool by program address; EVM by the vault contract.
        var poolIdentifier = chain.Family == ChainFamily.Solana
            ? chain.Deployment.Program
            : chain.Deployment.LiquidVault;

        var recordedAt = DateTime.UtcNow;

        return StakingLedgerEntry.Create(chain.Key, transactionId, verification.OperationIndex, entry =>
        {
            entry.WalletAddress = walletIdentifier;
            entry.Family = chain.FamilyName;
            entry.ActionType = ToActionType(operation);
            // Reward funding moves no principal, so the headline amount falls back to the reward.
            entry.Amount = verification.AssetAmount != 0m ? verification.AssetAmount : verification.RewardAmount;
            entry.AssetAmount = verification.AssetAmount;
            entry.ShareAmount = verification.ShareAmount;
            entry.RewardAmount = verification.RewardAmount;
            entry.RawAssetAmount = StakingAmountRules.ToRawAmount(verification.AssetAmount, chain.Deployment.CafeDecimals);
            entry.RawShareAmount = StakingAmountRules.ToRawAmount(verification.ShareAmount, chain.Deployment.StCafeDecimals);
            entry.RawRewardAmount = StakingAmountRules.ToRawAmount(verification.RewardAmount, chain.Deployment.CoffeeDecimals);
            entry.ChainId = chain.EvmChainId ?? 0;
            entry.NetworkName = chain.DisplayName;
            entry.PaymentTokenContract = chain.Deployment.Cafe;
            entry.StakingPoolContract = poolIdentifier;
            entry.AssetIdentifier = chain.Deployment.Cafe;
            entry.ReceiptIdentifier = chain.Deployment.StCafe;
            entry.RewardIdentifier = chain.Deployment.Coffee;
            entry.VaultOrProgramIdentifier = poolIdentifier;
            entry.BlockOrSlot = verification.BlockNumber;
            entry.Verified = true;
            entry.VerificationState = "verified";
            entry.ExplorerUrl = string.Format(chain.ExplorerTransactionTemplate, transactionId);
            entry.RecordedAtUtc = recordedAt;
            entry.OccurredAtUtc = recordedAt;
        });
    }

    private static string ToActionType(LiquidStakingOperation operation) => operation switch
    {
        LiquidStakingOperation.Deposit => "deposit",
        LiquidStakingOperation.Redeem => "redeem",
        LiquidStakingOperation.Claim => "claim",
        _ => "reward_funding"
    };
}
