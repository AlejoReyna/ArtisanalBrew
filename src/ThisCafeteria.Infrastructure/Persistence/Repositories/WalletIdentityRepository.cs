using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Repositories;

public sealed class WalletIdentityRepository(AppDbContext dbContext) : IWalletIdentityRepository
{
    public Task<WalletIdentity?> FindAsync(
        string family,
        string normalizedAddress,
        CancellationToken cancellationToken = default) =>
        dbContext.WalletIdentities
            .AsNoTracking()
            .SingleOrDefaultAsync(
                identity => identity.Family == family && identity.NormalizedAddress == normalizedAddress,
                cancellationToken);

    public async Task<bool> UpsertAsync(
        string userId,
        string family,
        string normalizedAddress,
        string displayAddress,
        string? walletProvider,
        CancellationToken cancellationToken = default)
    {
        // Tracked, not AsNoTracking: this row is about to be updated in place.
        var identity = await dbContext.WalletIdentities
            .FirstOrDefaultAsync(
                item => item.Family == family && item.NormalizedAddress == normalizedAddress,
                cancellationToken)
            .ConfigureAwait(false);

        if (identity is null)
        {
            dbContext.WalletIdentities.Add(new WalletIdentity
            {
                UserId = userId,
                Family = family,
                NormalizedAddress = normalizedAddress,
                DisplayAddress = displayAddress,
                WalletProvider = string.IsNullOrWhiteSpace(walletProvider) ? "unknown" : walletProvider,
                VerifiedAtUtc = DateTimeOffset.UtcNow
            });
        }
        else
        {
            if (!string.Equals(identity.UserId, userId, StringComparison.Ordinal))
            {
                return false;
            }

            identity.DisplayAddress = displayAddress;
            identity.WalletProvider = string.IsNullOrWhiteSpace(walletProvider)
                ? identity.WalletProvider
                : walletProvider;
            identity.VerifiedAtUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
