using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Repositories;

/// <summary>
/// Stores the single-use challenges a wallet signs to prove ownership.
/// </summary>
public interface IWalletAuthChallengeRepository
{
    /// <summary>
    /// Deletes challenges that are long expired or long consumed, in bounded batches so a large
    /// backlog never turns one login into an unbounded delete.
    /// </summary>
    Task PruneAsync(
        DateTimeOffset now,
        int maxRows = 1000,
        CancellationToken cancellationToken = default);

    Task AddAsync(WalletAuthChallenge challenge, CancellationToken cancellationToken = default);

    Task<WalletAuthChallenge?> FindByNonceHashAsync(
        string nonceHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a challenge consumed, returning <c>false</c> if it was already consumed or has
    /// expired.
    ///
    /// This must be a single conditional update rather than a read-then-write: it is the only
    /// thing preventing two concurrent requests from both redeeming one signature, and the
    /// atomicity is the whole point of the method.
    /// </summary>
    Task<bool> TryConsumeAsync(
        string nonceHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
