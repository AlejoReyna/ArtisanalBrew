using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services.Blockchain;

/// <summary>
/// How a record attempt ended. <see cref="Recorded"/> and <see cref="AlreadyRecorded"/> are both
/// successes - recording is idempotent, so a repeat submission of the same operation is a normal
/// outcome rather than an error.
/// </summary>
public enum LiquidStakingRecordStatus
{
    Recorded,
    AlreadyRecorded,
    PendingConfirmations,
    VerificationFailed,
    Conflict
}

public sealed record LiquidStakingRecordResult(
    LiquidStakingRecordStatus Status,
    StakingLedgerEntry? Entry = null,
    string? Error = null)
{
    public static LiquidStakingRecordResult Recorded(StakingLedgerEntry entry) =>
        new(LiquidStakingRecordStatus.Recorded, entry);

    public static LiquidStakingRecordResult AlreadyRecorded(StakingLedgerEntry entry) =>
        new(LiquidStakingRecordStatus.AlreadyRecorded, entry);

    public static readonly LiquidStakingRecordResult PendingConfirmations =
        new(LiquidStakingRecordStatus.PendingConfirmations);

    public static LiquidStakingRecordResult VerificationFailed(string? error) =>
        new(LiquidStakingRecordStatus.VerificationFailed, Error: error);

    public static readonly LiquidStakingRecordResult Conflict =
        new(LiquidStakingRecordStatus.Conflict);
}

/// <summary>
/// Verifies a liquid-staking transaction on chain and records it in the staking ledger.
///
/// This is the half of the old <c>LiquidStakingController.Record</c> that is not about HTTP: the
/// verification call, the mapping from a verified transaction to a ledger entry, and idempotent
/// persistence. The controller keeps chain resolution, session checks, and status codes.
/// </summary>
public interface ILiquidStakingLedgerService
{
    Task<LiquidStakingRecordResult> RecordAsync(
        ChainDefinition chain,
        string walletIdentifier,
        string transactionId,
        LiquidStakingOperation operation,
        decimal? expectedAmount,
        CancellationToken cancellationToken = default);
}
