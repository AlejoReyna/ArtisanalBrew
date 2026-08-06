using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Repositories;

/// <summary>
/// The outcome of an idempotent staking-ledger write. Three cases, and callers generally want to
/// treat the first two the same way:
///
/// <list type="bullet">
/// <item><c>Added: true</c> - the entry was written by this call.</item>
/// <item><c>Added: false, Existing: not null</c> - the operation was already recorded, either
/// before this call or by a concurrent one. The existing row is returned so the caller can
/// respond with the same payload it would have produced itself.</item>
/// <item><c>Added: false, Existing: null</c> - the write was rejected and the conflicting row
/// could not be read back. This is a genuine failure; callers should surface a conflict.</item>
/// </list>
/// </summary>
public sealed record StakingLedgerWriteResult(bool Added, StakingLedgerEntry? Existing)
{
    public static StakingLedgerWriteResult Written() => new(true, null);

    public static StakingLedgerWriteResult AlreadyPresent(StakingLedgerEntry existing) =>
        new(false, existing);

    public static StakingLedgerWriteResult Conflict() => new(false, null);
}
