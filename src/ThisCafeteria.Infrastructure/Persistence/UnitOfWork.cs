using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ThisCafeteria.Application.Repositories;

namespace ThisCafeteria.Infrastructure.Persistence;

/// <summary>
/// Transactions over the request-scoped <see cref="AppDbContext"/>.
///
/// This deliberately shares the scoped context with the repositories rather than creating its own,
/// because a transaction is only meaningful if the writes it is meant to cover run through the
/// same connection. Repositories that take <c>IDbContextFactory</c> instead - the staking ledger,
/// for one - are therefore <b>not</b> covered by this, and must not be mixed into a unit of work.
/// </summary>
public sealed class UnitOfWork(AppDbContext dbContext) : IUnitOfWork
{
    public async Task<IUnitOfWorkTransaction> BeginSerializableTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        return new UnitOfWorkTransaction(transaction);
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        return new UnitOfWorkTransaction(transaction);
    }

    private sealed class UnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
