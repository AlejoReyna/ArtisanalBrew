using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence;

/// <summary>
/// EF-backed implementation of the reconciliation worker's staged projection batch.
/// It only mutates the shared change tracker; the worker owns SaveChanges and the transaction.
/// </summary>
public sealed class AgenticCommerceProjectionBatch(AppDbContext db) : IAgenticCommerceProjectionBatch
{
    public Task<bool> HasAppliedEventAsync(
        string chainKey,
        string contractAddress,
        string transactionHash,
        int logIndex,
        CancellationToken cancellationToken) =>
        db.AgenticJobAppliedEvents.AnyAsync(
            entry => entry.ChainKey == chainKey
                && entry.ContractAddress == contractAddress
                && entry.TransactionHash == transactionHash
                && entry.LogIndex == logIndex,
            cancellationToken);

    public Task<bool> HasDeferredEventAsync(
        string chainKey,
        string contractAddress,
        string transactionHash,
        int logIndex,
        CancellationToken cancellationToken) =>
        db.AgenticJobDeferredEvents.AnyAsync(
            entry => entry.ChainKey == chainKey
                && entry.ContractAddress == contractAddress
                && entry.TransactionHash == transactionHash
                && entry.LogIndex == logIndex,
            cancellationToken);

    public async Task<AgenticJobProjection?> FindJobAsync(
        string chainKey,
        string contractAddress,
        long onChainJobId,
        CancellationToken cancellationToken)
    {
        var staged = db.AgenticJobs.Local.FirstOrDefault(job =>
            job.ChainKey == chainKey
            && job.ContractAddress == contractAddress
            && job.OnChainJobId == onChainJobId);

        return staged ?? await db.AgenticJobs.FirstOrDefaultAsync(job =>
            job.ChainKey == chainKey
            && job.ContractAddress == contractAddress
            && job.OnChainJobId == onChainJobId,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> HasAgentDirectoryEntryAsync(
        string chainKey,
        string registryAddress,
        long agentId,
        CancellationToken cancellationToken) =>
        db.AgentDirectoryEntries.AnyAsync(entry =>
            entry.ChainKey == chainKey
            && entry.RegistryAddress == registryAddress
            && entry.AgentId == agentId,
            cancellationToken);

    public void StageJob(AgenticJobProjection job) => db.AgenticJobs.Add(job);

    public void StageAgentDirectoryEntry(AgentDirectoryEntry entry) => db.AgentDirectoryEntries.Add(entry);

    public void StageAppliedEvent(AgenticJobAppliedEvent appliedEvent) => db.AgenticJobAppliedEvents.Add(appliedEvent);

    public void StageDeferredEvent(AgenticJobDeferredEvent deferredEvent) => db.AgenticJobDeferredEvents.Add(deferredEvent);
}
