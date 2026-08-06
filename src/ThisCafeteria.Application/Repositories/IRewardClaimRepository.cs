using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Repositories;

public interface IRewardClaimRepository
{
    Task<RewardClaim?> GetLatestDailyClaimAsync(string walletAddress, CancellationToken cancellationToken = default);

    Task AddAsync(RewardClaim claim, CancellationToken cancellationToken = default);

    /// <summary>Whether any claim already refers to this payment transaction.</summary>
    Task<bool> ExistsByPaymentHashAsync(
        string paymentTransactionHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a claim, returning <c>false</c> if the payment transaction has already been claimed.
    /// Distinct from <see cref="AddAsync"/> so that the duplicate-key case is a return value
    /// rather than a provider-specific exception crossing the layer boundary.
    /// </summary>
    Task<bool> TryAddAsync(RewardClaim claim, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to a claim already loaded in this unit of work.</summary>
    Task UpdateAsync(RewardClaim claim, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RewardClaim>> ListByWalletAsync(
        string walletAddress,
        int take = 20,
        CancellationToken cancellationToken = default);
}
