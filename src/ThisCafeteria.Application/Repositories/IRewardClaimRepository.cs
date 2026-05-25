using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Repositories;

public interface IRewardClaimRepository
{
    Task<RewardClaim?> GetLatestDailyClaimAsync(string walletAddress, CancellationToken cancellationToken = default);

    Task AddAsync(RewardClaim claim, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RewardClaim>> ListByWalletAsync(
        string walletAddress,
        int take = 20,
        CancellationToken cancellationToken = default);
}
