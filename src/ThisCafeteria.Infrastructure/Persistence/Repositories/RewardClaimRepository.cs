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

    public Task<bool> ExistsByPaymentHashAsync(
        string paymentTransactionHash,
        CancellationToken cancellationToken = default) =>
        dbContext.RewardClaims.AnyAsync(
            claim => claim.PaymentTransactionHash == paymentTransactionHash,
            cancellationToken);

    public async Task<bool> TryAddAsync(RewardClaim claim, CancellationToken cancellationToken = default)
    {
        dbContext.RewardClaims.Add(claim);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    public Task UpdateAsync(RewardClaim claim, CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);

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
