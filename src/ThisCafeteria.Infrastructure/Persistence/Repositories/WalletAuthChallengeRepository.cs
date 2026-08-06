using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Repositories;

public sealed class WalletAuthChallengeRepository(AppDbContext dbContext) : IWalletAuthChallengeRepository
{
    public Task PruneAsync(
        DateTimeOffset now,
        int maxRows = 1000,
        CancellationToken cancellationToken = default)
    {
        var expiredBefore = now.AddMinutes(-1);
        var consumedBefore = now.AddMinutes(-10);

        return dbContext.WalletAuthChallenges
            .Where(challenge => challenge.ExpiresAtUtc < expiredBefore
                || challenge.ConsumedAtUtc != null && challenge.ConsumedAtUtc < consumedBefore)
            .OrderBy(challenge => challenge.ExpiresAtUtc)
            .Take(maxRows)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task AddAsync(WalletAuthChallenge challenge, CancellationToken cancellationToken = default)
    {
        dbContext.WalletAuthChallenges.Add(challenge);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<WalletAuthChallenge?> FindByNonceHashAsync(
        string nonceHash,
        CancellationToken cancellationToken = default) =>
        dbContext.WalletAuthChallenges.SingleOrDefaultAsync(
            challenge => challenge.NonceHash == nonceHash,
            cancellationToken);

    /// <summary>
    /// One conditional UPDATE. The <c>ConsumedAtUtc == null</c> and <c>ExpiresAtUtc &gt; now</c>
    /// predicates are evaluated by the database as part of the write, so exactly one of two
    /// concurrent callers can see a row count of 1.
    /// </summary>
    public async Task<bool> TryConsumeAsync(
        string nonceHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var consumed = await dbContext.WalletAuthChallenges
            .Where(challenge => challenge.NonceHash == nonceHash
                && challenge.ConsumedAtUtc == null
                && challenge.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(challenge => challenge.ConsumedAtUtc, now)
                    .SetProperty(challenge => challenge.VerificationAttempts, challenge => challenge.VerificationAttempts + 1),
                cancellationToken)
            .ConfigureAwait(false);

        return consumed == 1;
    }
}
