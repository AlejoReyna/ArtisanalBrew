using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services.Blockchain;

public enum StakingRecordStatus
{
    Recorded,

    /// <summary>
    /// The transaction was already in the ledger. Note this is <b>not</b> treated as a success
    /// here, unlike the liquid-staking path: the single-chain staking endpoints have always
    /// answered a repeat submission with a conflict, and that behaviour is preserved.
    /// </summary>
    AlreadyRecorded,

    PendingConfirmations,
    VerificationFailed
}

public sealed record StakingRecordResult(
    StakingRecordStatus Status,
    StakingLedgerEntry? Entry = null,
    StakingVerificationResult? Verification = null)
{
    public static StakingRecordResult Recorded(StakingLedgerEntry entry) =>
        new(StakingRecordStatus.Recorded, entry);

    public static readonly StakingRecordResult AlreadyRecorded =
        new(StakingRecordStatus.AlreadyRecorded);

    public static StakingRecordResult PendingConfirmations(StakingVerificationResult verification) =>
        new(StakingRecordStatus.PendingConfirmations, Verification: verification);

    public static readonly StakingRecordResult VerificationFailed =
        new(StakingRecordStatus.VerificationFailed);
}

/// <summary>
/// Verifies a stake, unstake, or claim transaction on the configured EVM network and records it
/// in the staking ledger.
///
/// This covers the single-chain staking endpoints. The multi-chain liquid-staking equivalent is
/// <see cref="ILiquidStakingLedgerService"/>; the two differ in how they treat a duplicate
/// submission, so they are deliberately kept apart rather than merged.
/// </summary>
public interface IStakingLedgerService
{
    Task<StakingRecordResult> RecordAsync(
        BlockchainNetworkOptions chain,
        string walletAddress,
        string transactionHash,
        StakingTransactionType transactionType,
        decimal? expectedAmount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A wallet's most recent ledger activity, newest first - what the yield panel lists.
    /// Returns an empty list for a blank wallet rather than throwing, since the panel renders
    /// before a wallet is connected.
    /// </summary>
    Task<IReadOnlyList<StakingLedgerEntry>> GetRecentActivityAsync(
        string walletAddress,
        int take = 8,
        CancellationToken cancellationToken = default);
}
