using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Repositories;

public sealed class RewardClaimRepository(AppDbContext dbContext) : IRewardClaimRepository
{
    public Task<RewardClaim?> GetLatestDailyClaimAsync(
        string walletAddress,
        CancellationToken cancellationToken = default) =>
        dbContext.RewardClaims
            .AsNoTracking()
            .Where(claim =>
                claim.WalletAddress == walletAddress &&
                claim.ClaimType == "daily")
            .OrderByDescending(claim => claim.ClaimedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(RewardClaim claim, CancellationToken cancellationToken = default)
    {
        dbContext.RewardClaims.Add(claim);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RewardClaim>> ListByWalletAsync(
        string walletAddress,
        int take = 20,
        CancellationToken cancellationToken = default) =>
        await dbContext.RewardClaims
            .AsNoTracking()
            .Where(claim => claim.WalletAddress == walletAddress)
            .OrderByDescending(claim => claim.ClaimedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
