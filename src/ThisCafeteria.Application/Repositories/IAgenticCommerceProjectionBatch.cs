using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Repositories;

/// <summary>
/// The staged projection writes made while reconciling one chain range.
///
/// Implementations must attach changes to the caller's unit of work, but must not persist them.
/// The reconciliation worker commits the projections and its checkpoint together in one
/// transaction after every event in the range has been staged.
/// </summary>
public interface IAgenticCommerceProjectionBatch
{
    Task<bool> HasAppliedEventAsync(
        string chainKey,
        string contractAddress,
        string transactionHash,
        int logIndex,
        CancellationToken cancellationToken);

    Task<bool> HasDeferredEventAsync(
        string chainKey,
        string contractAddress,
        string transactionHash,
        int logIndex,
        CancellationToken cancellationToken);

    Task<AgenticJobProjection?> FindJobAsync(
        string chainKey,
        string contractAddress,
        long onChainJobId,
        CancellationToken cancellationToken);

    Task<bool> HasAgentDirectoryEntryAsync(
        string chainKey,
        string registryAddress,
        long agentId,
        CancellationToken cancellationToken);

    void StageJob(AgenticJobProjection job);
    void StageAgentDirectoryEntry(AgentDirectoryEntry entry);
    void StageAppliedEvent(AgenticJobAppliedEvent appliedEvent);
    void StageDeferredEvent(AgenticJobDeferredEvent deferredEvent);
}
