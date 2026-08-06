namespace ThisCafeteria.Application.Repositories;

/// <summary>
/// Groups several repository writes into one atomic unit.
///
/// Most operations in this codebase do not need it - the repositories save as they go, and a
/// single write is already atomic. This exists for the cases that genuinely span more than one
/// write and must roll back together, where a per-entity repository cannot express the guarantee
/// on its own.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Begins a serializable transaction. Serializable specifically, rather than the provider
    /// default: the callers that need this are guarding against a concurrent request claiming the
    /// same on-chain transaction, and a weaker level would let both through.
    /// </summary>
    Task<IUnitOfWorkTransaction> BeginSerializableTransactionAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a transaction at the provider's default isolation level, for callers that need
    /// several writes to commit together but are not guarding against a specific race.
    /// </summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// An open transaction. Disposing without committing rolls back - which is how callers abandon
/// work after a failure, rather than by calling an explicit rollback.
/// </summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
