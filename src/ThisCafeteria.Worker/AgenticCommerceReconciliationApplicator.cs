using System.Numerics;
using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.Worker;

public enum EscrowEventType { JobCreated, ProviderSet, BudgetSet, JobFunded, JobSubmitted, JobCompleted, JobRejected, JobExpired, PaymentReleased, Refunded }

public sealed class EscrowEvent
{
    public EscrowEventType Type { get; init; }
    public long OnChainJobId { get; init; }
    public string Client { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Evaluator { get; init; } = string.Empty;
    public BigInteger ExpiredAt { get; init; }
    public BigInteger Amount { get; init; }
    public string Deliverable { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string TransactionHash { get; init; } = string.Empty;
    public long BlockNumber { get; init; }
    public int LogIndex { get; init; }
}

public sealed class RegistryEvent
{
    public long AgentId { get; init; }
    public string Owner { get; init; } = string.Empty;
    public string MetadataURI { get; init; } = string.Empty;
    public string TransactionHash { get; init; } = string.Empty;
    public long BlockNumber { get; init; }
    public int LogIndex { get; init; }
}

public interface IAgenticCommerceReconciliationApplicator
{
    Task ApplyEventAsync(AppDbContext db, ChainDefinition chain, string escrowAddress, EscrowEvent evt, CancellationToken cancellationToken);
    Task ApplyRegistryEventAsync(AppDbContext db, ChainDefinition chain, string registryAddress, RegistryEvent evt, CancellationToken cancellationToken);
}

/// <summary>
/// Applies escrow events to the AgenticJobProjection read model.
///
/// Consistency policy:
///
///   1. Duplicate events (same ChainKey + ContractAddress + TransactionHash + LogIndex):
///      idempotent – return immediately without re-applying.
///
///   2. Missing prerequisite (job not yet in the projection):
///      Record a durable AgenticJobDeferredEvent and return.  The checkpoint must NOT
///      advance past this range until persistence succeeds.
///
///   3. Invalid state transition (job exists but in the wrong status):
///      - Truly idempotent duplicates of terminal events are silently skipped.
///      - All other wrong-order events are recorded as deferred (same mechanism as #2).
///
///   4. Wrong escrow address:
///      FindJobAsync includes ContractAddress in its WHERE clause, so a cross-escrow event
///      naturally returns null and is recorded as deferred (same mechanism as #2).
///
///   5. Checkpoint atomicity:
///      The caller (ReconcileOnceAsync) wraps all ApplyEventAsync calls in a single DB
///      transaction. If SaveChanges or CommitAsync fails the checkpoint stays unchanged.
///      ApplyEventAsync itself does NOT call SaveChanges.
/// </summary>
public class AgenticCommerceReconciliationApplicator : IAgenticCommerceReconciliationApplicator
{
    public async Task ApplyEventAsync(AppDbContext db, ChainDefinition chain, string escrowAddress, EscrowEvent evt, CancellationToken cancellationToken)
    {
        var chainId = chain.EvmChainId ?? 0;

        // --- Guard: already applied (idempotent) ---
        var alreadyApplied = await db.AgenticJobAppliedEvents.AnyAsync(
            e => e.ChainKey == chain.Key && e.ContractAddress == escrowAddress && e.TransactionHash == evt.TransactionHash && e.LogIndex == evt.LogIndex,
            cancellationToken).ConfigureAwait(false);

        if (alreadyApplied) return;

        // --- Guard: already deferred (avoid duplicate deferred rows) ---
        var alreadyDeferred = await db.AgenticJobDeferredEvents.AnyAsync(
            e => e.ChainKey == chain.Key && e.ContractAddress == escrowAddress && e.TransactionHash == evt.TransactionHash && e.LogIndex == evt.LogIndex,
            cancellationToken).ConfigureAwait(false);

        switch (evt.Type)
        {
            case EscrowEventType.JobCreated:
                {
                    var existingJob = await FindJobAsync(db, chain.Key, escrowAddress, evt.OnChainJobId, cancellationToken);
                    if (existingJob != null)
                    {
                        // Idempotent: same on-chain identity already projected.
                        // Still record the applied-event so this log identity is never re-processed.
                        break;
                    }

                    db.AgenticJobs.Add(new AgenticJobProjection
                    {
                        ChainKey = chain.Key,
                        OnChainJobId = evt.OnChainJobId,
                        ChainId = chainId,
                        ContractAddress = escrowAddress,
                        EscrowAddress = escrowAddress,
                        ClientAddress = evt.Client,
                        ProviderAddress = evt.Provider,
                        EvaluatorAddress = evt.Evaluator,
                        ExpiredAt = (long)evt.ExpiredAt,
                        Status = AgenticJobProjection.StatusOpen,
                        CreationTransactionHash = evt.TransactionHash,
                        LastReconciledBlock = evt.BlockNumber,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    });
                    break;
                }

            case EscrowEventType.ProviderSet:
                {
                    var job = await FindJobAsync(db, chain.Key, escrowAddress, evt.OnChainJobId, cancellationToken);
                    if (job == null)
                    {
                        if (!alreadyDeferred)
                            RecordDeferred(db, chain.Key, escrowAddress, evt, "Job not found for ProviderSet");
                        return; // Do NOT record applied-event; checkpoint may not advance past this silently.
                    }
                    if (job.Status != AgenticJobProjection.StatusOpen)
                    {
                        if (!alreadyDeferred)
                            RecordDeferred(db, chain.Key, escrowAddress, evt, $"Invalid state {job.Status} for ProviderSet");
                        return;
                    }
                    job.ProviderAddress = evt.Provider;
                    job.LastReconciledBlock = evt.BlockNumber;
                    job.UpdatedAtUtc = DateTime.UtcNow;
                    job.ConcurrencyToken++;
                    break;
                }

            case EscrowEventType.BudgetSet:
                {
                    var job = await FindJobAsync(db, chain.Key, escrowAddress, evt.OnChainJobId, cancellationToken);
                    if (job == null)
                    {
                        if (!alreadyDeferred)
                            RecordDeferred(db, chain.Key, escrowAddress, evt, "Job not found for BudgetSet");
                        return;
                    }
                    if (job.Status != AgenticJobProjection.StatusOpen)
                    {
                        // BudgetSet on a non-open job is silently ignored (budget is already set via fund).
                        break;
                    }
                    job.Budget = (decimal)evt.Amount / 1_000_000_000_000_000_000m;
                    job.LastReconciledBlock = evt.BlockNumber;
                    job.UpdatedAtUtc = DateTime.UtcNow;
                    job.ConcurrencyToken++;
                    break;
                }

            case EscrowEventType.JobFunded:
                {
                    var job = await FindJobAsync(db, chain.Key, escrowAddress, evt.OnChainJobId, cancellationToken);
                    if (job == null)
                    {
                        if (!alreadyDeferred)
                            RecordDeferred(db, chain.Key, escrowAddress, evt, "Job not found for JobFunded");
                        return;
                    }
                    if (job.Status != AgenticJobProjection.StatusOpen)
                    {
                        // Duplicate fund on an already-funded/terminal job: idempotent skip.
                        break;
                    }
                    job.Status = AgenticJobProjection.StatusFunded;
                    job.Budget = (decimal)evt.Amount / 1_000_000_000_000_000_000m;
                    job.FundedTransactionHash = evt.TransactionHash;
                    job.LastReconciledBlock = evt.BlockNumber;
                    job.UpdatedAtUtc = DateTime.UtcNow;
                    job.ConcurrencyToken++;
                    break;
                }

            case EscrowEventType.JobSubmitted:
                {
                    var job = await FindJobAsync(db, chain.Key, escrowAddress, evt.OnChainJobId, cancellationToken);
                    if (job == null)
                    {
                        if (!alreadyDeferred)
                            RecordDeferred(db, chain.Key, escrowAddress, evt, "Job not found for JobSubmitted");
                        return;
                    }
                    if (job.Status != AgenticJobProjection.StatusFunded)
                    {
                        if (!alreadyDeferred)
                            RecordDeferred(db, chain.Key, escrowAddress, evt, $"Invalid state {job.Status} for JobSubmitted (expected Funded)");
                        return;
                    }
                    job.Status = AgenticJobProjection.StatusSubmitted;
                    job.DeliverableCommitment = evt.Deliverable;
                    job.LastReconciledBlock = evt.BlockNumber;
                    job.UpdatedAtUtc = DateTime.UtcNow;
                    job.ConcurrencyToken++;
                    break;
                }

            case EscrowEventType.JobCompleted:
                {
                    var job = await FindJobAsync(db, chain.Key, escrowAddress, evt.OnChainJobId, cancellationToken);
                    if (job == null)
                    {
                        if (!alreadyDeferred)
                            RecordDeferred(db, chain.Key, escrowAddress, evt, "Job not found for JobCompleted");
                        return;
                    }
                    if (job.Status == AgenticJobProjection.StatusCompleted)
                    {
                        // Idempotent duplicate terminal event.
                        break;
                    }
                    if (job.Status != AgenticJobProjection.StatusSubmitted)
                    {
                        if (!alreadyDeferred)
                            RecordDeferred(db, chain.Key, escrowAddress, evt, $"Invalid state {job.Status} for JobCompleted (expected Submitted)");
                        return;
                    }
                    job.Status = AgenticJobProjection.StatusCompleted;
                    job.DecisionReason = evt.Reason;
                    job.CompletionTransactionHash = evt.TransactionHash;
                    job.LastReconciledBlock = evt.BlockNumber;
                    job.UpdatedAtUtc = DateTime.UtcNow;
                    job.ConcurrencyToken++;
                    break;
                }

            case EscrowEventType.JobRejected:
                {
                    var job = await FindJobAsync(db, chain.Key, escrowAddress, evt.OnChainJobId, cancellationToken);
                    if (job == null)
                    {
                        if (!alreadyDeferred)
                            RecordDeferred(db, chain.Key, escrowAddress, evt, "Job not found for JobRejected");
                        return;
                    }
                    if (job.Status is AgenticJobProjection.StatusCompleted or AgenticJobProjection.StatusRejected or AgenticJobProjection.StatusExpired)
                    {
                        // Idempotent duplicate terminal event.
                        break;
                    }
                    if (job.Status != AgenticJobProjection.StatusSubmitted)
                    {
                        if (!alreadyDeferred)
                            RecordDeferred(db, chain.Key, escrowAddress, evt, $"Invalid state {job.Status} for JobRejected (expected Submitted)");
                        return;
                    }
                    job.Status = AgenticJobProjection.StatusRejected;
                    job.DecisionReason = evt.Reason;
                    job.CompletionTransactionHash = evt.TransactionHash;
                    job.LastReconciledBlock = evt.BlockNumber;
                    job.UpdatedAtUtc = DateTime.UtcNow;
                    job.ConcurrencyToken++;
                    break;
                }

            case EscrowEventType.JobExpired:
                {
                    var job = await FindJobAsync(db, chain.Key, escrowAddress, evt.OnChainJobId, cancellationToken);
                    if (job == null)
                    {
                        if (!alreadyDeferred)
                            RecordDeferred(db, chain.Key, escrowAddress, evt, "Job not found for JobExpired");
                        return;
                    }
                    if (job.Status == AgenticJobProjection.StatusExpired)
                    {
                        // Idempotent duplicate terminal event.
                        break;
                    }
                    if (job.Status != AgenticJobProjection.StatusFunded)
                    {
                        if (!alreadyDeferred)
                            RecordDeferred(db, chain.Key, escrowAddress, evt, $"Invalid state {job.Status} for JobExpired (expected Funded)");
                        return;
                    }
                    job.Status = AgenticJobProjection.StatusExpired;
                    job.CompletionTransactionHash = evt.TransactionHash;
                    job.LastReconciledBlock = evt.BlockNumber;
                    job.UpdatedAtUtc = DateTime.UtcNow;
                    job.ConcurrencyToken++;
                    break;
                }

            case EscrowEventType.PaymentReleased:
            case EscrowEventType.Refunded:
                {
                    var job = await FindJobAsync(db, chain.Key, escrowAddress, evt.OnChainJobId, cancellationToken);
                    if (job == null)
                    {
                        if (!alreadyDeferred)
                            RecordDeferred(db, chain.Key, escrowAddress, evt, $"Job not found for {evt.Type}");
                        return;
                    }
                    job.LastReconciledBlock = evt.BlockNumber;
                    job.UpdatedAtUtc = DateTime.UtcNow;
                    job.ConcurrencyToken++;
                    break;
                }
        }

        // Record that this log identity was processed (either applied or idempotent skip).
        // This must only be reached by paths that do NOT early-return via deferred-event recording.
        db.AgenticJobAppliedEvents.Add(new AgenticJobAppliedEvent
        {
            ChainKey = chain.Key,
            ContractAddress = escrowAddress,
            TransactionHash = evt.TransactionHash,
            LogIndex = evt.LogIndex
        });
    }

    public async Task ApplyRegistryEventAsync(AppDbContext db, ChainDefinition chain, string registryAddress, RegistryEvent evt, CancellationToken cancellationToken)
    {
        var alreadyApplied = await db.AgenticJobAppliedEvents.AnyAsync(
            e => e.ChainKey == chain.Key && e.ContractAddress == registryAddress && e.TransactionHash == evt.TransactionHash && e.LogIndex == evt.LogIndex,
            cancellationToken).ConfigureAwait(false);

        if (alreadyApplied) return;

        var exists = await db.AgentDirectoryEntries.AnyAsync(
            a => a.ChainKey == chain.Key && a.AgentId == evt.AgentId && a.RegistryAddress == registryAddress,
            cancellationToken).ConfigureAwait(false);

        if (!exists)
        {
            db.AgentDirectoryEntries.Add(new AgentDirectoryEntry
            {
                ChainKey = chain.Key,
                AgentId = evt.AgentId,
                RegistryAddress = registryAddress,
                OwnerAddress = evt.Owner,
                MetadataUri = evt.MetadataURI,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        db.AgenticJobAppliedEvents.Add(new AgenticJobAppliedEvent
        {
            ChainKey = chain.Key,
            ContractAddress = registryAddress,
            TransactionHash = evt.TransactionHash,
            LogIndex = evt.LogIndex
        });
    }

    // --- Helpers ---

    private static void RecordDeferred(AppDbContext db, string chainKey, string escrowAddress, EscrowEvent evt, string reason)
    {
        db.AgenticJobDeferredEvents.Add(new AgenticJobDeferredEvent
        {
            ChainKey = chainKey,
            ContractAddress = escrowAddress,
            OnChainJobId = evt.OnChainJobId,
            EventType = evt.Type.ToString(),
            TransactionHash = evt.TransactionHash,
            LogIndex = evt.LogIndex,
            BlockNumber = evt.BlockNumber,
            DeferralReason = reason
        });
    }

    private static async Task<AgenticJobProjection?> FindJobAsync(
        AppDbContext db, string chainKey, string escrowAddress, long onChainJobId,
        CancellationToken cancellationToken)
    {
        var local = db.AgenticJobs.Local.FirstOrDefault(j => j.ChainKey == chainKey && j.OnChainJobId == onChainJobId && j.ContractAddress == escrowAddress);
        if (local != null) return local;

        return await db.AgenticJobs.FirstOrDefaultAsync(
            j => j.ChainKey == chainKey && j.OnChainJobId == onChainJobId && j.ContractAddress == escrowAddress,
            cancellationToken).ConfigureAwait(false);
    }
}
