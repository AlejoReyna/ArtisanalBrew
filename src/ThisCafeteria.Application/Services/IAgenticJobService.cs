using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services;

public interface IAgenticJobService
{
    Task<List<AgentDirectoryEntry>> GetAgentsAsync(string chainKey);
    Task<List<AgenticJobProjection>> GetJobsAsync(string chainKey, string userAddress);
    Task<AgenticJobProjection?> GetJobAsync(Guid id);
    Task<AgenticJobProjection> CreateJobAsync(string chainKey, string clientAddress, string providerAddress, string evaluatorAddress, string descriptionCommitment, decimal budget, long expiredAt);
    Task AdvanceJobStatusAsync(Guid id, string expectedStatus, string newStatus, Action<AgenticJobProjection>? updateAction = null);

    /// <summary>
    /// Advance a job's status based on a verified on-chain event.
    /// Validates the expected state transition and records the transaction hash.
    /// Idempotent: re-processing the same event for a job already in the target state is a no-op.
    /// </summary>
    Task AdvanceJobWithTransactionAsync(Guid id, string expectedStatus, string newStatus, string transactionHash, Action<AgenticJobProjection>? updateAction = null);
}
