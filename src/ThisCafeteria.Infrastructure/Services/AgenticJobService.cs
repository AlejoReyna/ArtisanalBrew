using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;
using Nethereum.Util;

namespace ThisCafeteria.Infrastructure.Services;

public class AgenticJobService(AppDbContext dbContext, IChainRegistry chainRegistry) : IAgenticJobService
{
    public async Task<List<AgentDirectoryEntry>> GetAgentsAsync(string chainKey)
    {
        return await dbContext.AgentDirectoryEntries
            .Where(x => x.ChainKey == chainKey && x.IsActive)
            .ToListAsync();
    }

    public async Task<List<AgenticJobProjection>> GetJobsAsync(string chainKey, string userAddress)
    {
        var normalized = userAddress.ToLowerInvariant();
        return await dbContext.AgenticJobs
            .Where(x => x.ChainKey == chainKey && (x.ClientAddress.ToLower() == normalized || x.ProviderAddress.ToLower() == normalized || x.EvaluatorAddress.ToLower() == normalized))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync();
    }

    public async Task<AgenticJobProjection?> GetJobAsync(Guid id)
    {
        return await dbContext.AgenticJobs.FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<AgenticJobProjection> CreateJobAsync(string chainKey, string clientAddress, string providerAddress, string evaluatorAddress, string descriptionCommitment, decimal budget, long expiredAt)
    {
        if (!chainRegistry.TryGet(chainKey, out _)) throw new ArgumentException("Unsupported chain key.");
        if (!AddressUtil.Current.IsValidEthereumAddressHexFormat(clientAddress)) throw new ArgumentException("Invalid client address.");
        if (!string.IsNullOrWhiteSpace(providerAddress) && providerAddress != "0x0000000000000000000000000000000000000000" && !AddressUtil.Current.IsValidEthereumAddressHexFormat(providerAddress)) throw new ArgumentException("Invalid provider address.");
        if (!AddressUtil.Current.IsValidEthereumAddressHexFormat(evaluatorAddress)) throw new ArgumentException("Invalid evaluator address.");
        if (budget <= 0) throw new ArgumentException("Budget must be greater than zero.");
        if (DateTimeOffset.FromUnixTimeSeconds(expiredAt) <= DateTimeOffset.UtcNow) throw new ArgumentException("Expiry must be in the future.");
        if (descriptionCommitment?.Length > 256) throw new ArgumentException("Description commitment is too long.");

        var job = new AgenticJobProjection
        {
            ChainKey = chainKey,
            ClientAddress = clientAddress,
            ProviderAddress = providerAddress,
            EvaluatorAddress = evaluatorAddress,
            DescriptionCommitment = descriptionCommitment ?? string.Empty,
            Budget = budget,
            ExpiredAt = expiredAt,
            Status = AgenticJobProjection.StatusOpen,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        dbContext.AgenticJobs.Add(job);
        await dbContext.SaveChangesAsync();
        return job;
    }

    public async Task AdvanceJobStatusAsync(Guid id, string expectedStatus, string newStatus, Action<AgenticJobProjection>? updateAction = null)
    {
        var job = await dbContext.AgenticJobs.FirstOrDefaultAsync(x => x.Id == id);
        if (job == null)
            throw new InvalidOperationException($"Job {id} not found.");

        if (job.Status == AgenticJobProjection.StatusCompleted ||
            job.Status == AgenticJobProjection.StatusRejected ||
            job.Status == AgenticJobProjection.StatusExpired)
        {
            throw new InvalidOperationException("Terminal states are immutable.");
        }

        if (expectedStatus == "*")
            throw new InvalidOperationException("Wildcard transitions are not allowed.");

        if (job.Status != expectedStatus)
            throw new InvalidOperationException($"Invalid state transition: Job {id} is in status '{job.Status}', expected '{expectedStatus}'.");

        bool isValidTransition = (job.Status == AgenticJobProjection.StatusOpen && (newStatus == AgenticJobProjection.StatusFunded || newStatus == AgenticJobProjection.StatusRejected)) ||
                                 (job.Status == AgenticJobProjection.StatusFunded && (newStatus == AgenticJobProjection.StatusSubmitted || newStatus == AgenticJobProjection.StatusRejected || newStatus == AgenticJobProjection.StatusExpired)) ||
                                 (job.Status == AgenticJobProjection.StatusSubmitted && (newStatus == AgenticJobProjection.StatusCompleted || newStatus == AgenticJobProjection.StatusRejected || newStatus == AgenticJobProjection.StatusExpired));

        if (!isValidTransition)
            throw new InvalidOperationException($"Transition from {job.Status} to {newStatus} is not allowed.");

        job.Status = newStatus;
        job.UpdatedAtUtc = DateTime.UtcNow;
        job.ConcurrencyToken++;
        updateAction?.Invoke(job);

        await dbContext.SaveChangesAsync();
    }

    public async Task AdvanceJobWithTransactionAsync(Guid id, string expectedStatus, string newStatus, string transactionHash, Action<AgenticJobProjection>? updateAction = null)
    {
        var job = await dbContext.AgenticJobs.FirstOrDefaultAsync(x => x.Id == id);
        if (job == null)
            throw new InvalidOperationException($"Job {id} not found.");

        // Idempotent: if already in the target state, skip silently
        if (job.Status == newStatus) return;

        if (job.Status == AgenticJobProjection.StatusCompleted ||
            job.Status == AgenticJobProjection.StatusRejected ||
            job.Status == AgenticJobProjection.StatusExpired)
        {
            throw new InvalidOperationException("Terminal states are immutable.");
        }

        if (expectedStatus != "*" && job.Status != expectedStatus)
            throw new InvalidOperationException($"Invalid state transition: Job {id} is in status '{job.Status}', expected '{expectedStatus}'.");

        bool isValidTransition = (job.Status == AgenticJobProjection.StatusOpen && (newStatus == AgenticJobProjection.StatusFunded || newStatus == AgenticJobProjection.StatusRejected)) ||
                                 (job.Status == AgenticJobProjection.StatusFunded && (newStatus == AgenticJobProjection.StatusSubmitted || newStatus == AgenticJobProjection.StatusRejected || newStatus == AgenticJobProjection.StatusExpired)) ||
                                 (job.Status == AgenticJobProjection.StatusSubmitted && (newStatus == AgenticJobProjection.StatusCompleted || newStatus == AgenticJobProjection.StatusRejected || newStatus == AgenticJobProjection.StatusExpired));

        if (!isValidTransition)
            throw new InvalidOperationException($"Transition from {job.Status} to {newStatus} is not allowed.");

        job.Status = newStatus;
        job.UpdatedAtUtc = DateTime.UtcNow;
        job.ConcurrencyToken++;

        // Record which transaction hash caused this state change
        if (newStatus == AgenticJobProjection.StatusFunded)
            job.FundedTransactionHash = transactionHash;
        else if (newStatus is AgenticJobProjection.StatusCompleted or AgenticJobProjection.StatusRejected or AgenticJobProjection.StatusExpired)
            job.CompletionTransactionHash = transactionHash;

        updateAction?.Invoke(job);

        await dbContext.SaveChangesAsync();
    }
}

