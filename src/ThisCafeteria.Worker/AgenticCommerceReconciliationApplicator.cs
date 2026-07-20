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

public class AgenticCommerceReconciliationApplicator : IAgenticCommerceReconciliationApplicator
{
    public async Task ApplyEventAsync(AppDbContext db, ChainDefinition chain, string escrowAddress, EscrowEvent evt, CancellationToken cancellationToken)
    {
        var chainId = chain.EvmChainId ?? 0;
        
        var alreadyApplied = await db.AgenticJobAppliedEvents.AnyAsync(
            e => e.ChainKey == chain.Key && e.ContractAddress == escrowAddress && e.TransactionHash == evt.TransactionHash && e.LogIndex == evt.LogIndex,
            cancellationToken).ConfigureAwait(false);
            
        if (alreadyApplied) return;

        switch (evt.Type)
        {
            case EscrowEventType.JobCreated:
            {
                var existingJob = await FindJobAsync(db, chain.Key, escrowAddress, evt.OnChainJobId, cancellationToken);
                if (existingJob != null)
                {
                    return; // Idempotent: already exists
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
                if (job == null) throw new InvalidOperationException($"Job {evt.OnChainJobId} not found");
                if (job.Status != AgenticJobProjection.StatusOpen) throw new InvalidOperationException($"Invalid state for ProviderSet: {job.Status}");
                job.ProviderAddress = evt.Provider;
                job.LastReconciledBlock = evt.BlockNumber;
                job.UpdatedAtUtc = DateTime.UtcNow;
                job.ConcurrencyToken++;
                break;
            }
            case EscrowEventType.BudgetSet:
            {
                var job = await FindJobAsync(db, chain.Key, escrowAddress, evt.OnChainJobId, cancellationToken);
                if (job == null) return;
                if (job.Status != AgenticJobProjection.StatusOpen) return;
                job.Budget = (decimal)evt.Amount / 1_000_000_000_000_000_000m;
                job.LastReconciledBlock = evt.BlockNumber;
                job.UpdatedAtUtc = DateTime.UtcNow;
                job.ConcurrencyToken++;
                break;
            }
            case EscrowEventType.JobFunded:
            {
                var job = await FindJobAsync(db, chain.Key, escrowAddress, evt.OnChainJobId, cancellationToken);
                if (job == null) return;
                if (job.Status != AgenticJobProjection.StatusOpen) return;
                job.Status = AgenticJobProjection.StatusFunded;
                // Since Nethereum.Web3 is in the worker project and we extracted this to be tested,
                // we convert Wei to Ether manually or reference Nethereum.Web3
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
                if (job == null) return;
                if (job.Status != AgenticJobProjection.StatusFunded) return;
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
                if (job == null) return;
                if (job.Status != AgenticJobProjection.StatusSubmitted) return;
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
                if (job == null) return;
                if (job.Status is AgenticJobProjection.StatusCompleted or AgenticJobProjection.StatusRejected or AgenticJobProjection.StatusExpired) return;
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
                if (job == null) return;
                if (job.Status != AgenticJobProjection.StatusFunded) return;
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
                if (job == null) return;
                job.LastReconciledBlock = evt.BlockNumber;
                job.UpdatedAtUtc = DateTime.UtcNow;
                job.ConcurrencyToken++;
                break;
            }
        }
        
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
