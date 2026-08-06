using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Repositories;

/// <summary>
/// Backed by <see cref="IDbContextFactory{TContext}"/> rather than the scoped
/// <see cref="AppDbContext"/>, because this repository is read from Blazor components as well as
/// controllers.
///
/// A component's scope is the whole circuit, not a single request, so overlapping renders and
/// event handlers can use it concurrently - and a DbContext is not thread-safe. Taking a
/// short-lived context per operation is what makes the repository safe in both settings. This
/// preserves the property that <c>YieldPanel</c> previously got by injecting the factory itself;
/// see the note in <c>Web/Services/ProfileAvatarState.cs</c>.
/// </summary>
public sealed class StakingLedgerRepository(IDbContextFactory<AppDbContext> contextFactory)
    : IStakingLedgerRepository
{
    public async Task<bool> ExistsByTransactionHashAsync(
        string transactionHash,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await dbContext.StakingLedgerEntries
            .AnyAsync(entry => entry.TransactionHash == transactionHash, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<StakingLedgerEntry?> FindByOperationAsync(
        string chainKey,
        string transactionHash,
        int operationIndex,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await dbContext.StakingLedgerEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entry => entry.ChainKey == chainKey
                    && entry.TransactionHash == transactionHash
                    && entry.OperationIndex == operationIndex,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Insert-or-return. The pre-check is an optimisation, not the guarantee: two concurrent
    /// requests can both pass it, and the unique index is what actually keeps the ledger honest.
    /// The <see cref="DbUpdateException"/> catch is therefore the real idempotency path, and it
    /// belongs here rather than in a caller - it is an Entity Framework concern, and this is the
    /// only layer entitled to know that.
    /// </summary>
    public async Task<StakingLedgerWriteResult> AddIfAbsentAsync(
        StakingLedgerEntry entry,
        CancellationToken cancellationToken = default)
    {
        var operation = entry.OperationIdentity;
        var existing = await FindByOperationAsync(
                operation.ChainKey,
                operation.TransactionHash,
                operation.OperationIndex,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return StakingLedgerWriteResult.AlreadyPresent(existing);
        }

        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        dbContext.StakingLedgerEntries.Add(entry);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return StakingLedgerWriteResult.Written();
        }
        catch (DbUpdateException)
        {
            var concurrent = await FindByOperationAsync(
                    operation.ChainKey,
                    operation.TransactionHash,
                    operation.OperationIndex,
                    cancellationToken)
                .ConfigureAwait(false);

            return concurrent is not null
                ? StakingLedgerWriteResult.AlreadyPresent(concurrent)
                : StakingLedgerWriteResult.Conflict();
        }
    }

    public async Task<IReadOnlyList<StakingLedgerEntry>> ListByWalletAsync(
        string walletAddress,
        int take = 8,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await dbContext.StakingLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.WalletAddress == walletAddress)
            .OrderByDescending(entry => entry.RecordedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
