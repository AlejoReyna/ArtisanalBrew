using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Repositories;

public sealed class SolanaFaucetClaimRepository(AppDbContext dbContext) : ISolanaFaucetClaimRepository
{
    public Task<SolanaFaucetClaim?> FindLatestAsync(
        string chainKey,
        string walletAddress,
        CancellationToken cancellationToken = default) =>
        dbContext.SolanaFaucetClaims
            .AsNoTracking()
            .Where(claim => claim.ChainKey == chainKey && claim.WalletAddress == walletAddress)
            .OrderByDescending(claim => claim.ClaimedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(SolanaFaucetClaim claim, CancellationToken cancellationToken = default)
    {
        dbContext.SolanaFaucetClaims.Add(claim);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
