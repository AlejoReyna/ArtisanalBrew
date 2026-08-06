using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Repositories;

/// <summary>
/// Reads and writes the staking ledger - the record of verified on-chain stake, unstake, claim,
/// deposit, redeem and reward-funding operations.
/// </summary>
public interface IStakingLedgerRepository
{
    /// <summary>Whether any entry already records this transaction, on any chain.</summary>
    Task<bool> ExistsByTransactionHashAsync(
        string transactionHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The entry for one specific operation. A single transaction can carry several operations,
    /// so the identity of a ledger row is (chain, transaction, operation index) rather than the
    /// transaction hash alone.
    /// </summary>
    Task<StakingLedgerEntry?> FindByOperationAsync(
        string chainKey,
        string transactionHash,
        int operationIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an entry unless the same operation is already present.
    ///
    /// Callers race: the browser retries, and two requests can verify the same transaction
    /// concurrently. Rather than make every caller reason about that, this returns the winning
    /// row so recording stays idempotent. See <see cref="StakingLedgerWriteResult"/>.
    /// </summary>
    Task<StakingLedgerWriteResult> AddIfAbsentAsync(
        StakingLedgerEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>Most recent entries for a wallet, newest first.</summary>
    Task<IReadOnlyList<StakingLedgerEntry>> ListByWalletAsync(
        string walletAddress,
        int take = 8,
        CancellationToken cancellationToken = default);
}
