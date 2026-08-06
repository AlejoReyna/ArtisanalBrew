using FluentAssertions;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Application.Services.AgenticCommerce;
using ThisCafeteria.Domain.Entities;
using Xunit;

namespace ThisCafeteria.UnitTests;

public class AgenticCommerceReconciliationApplicatorPortTests
{
    [Fact]
    public async Task ApplyEvent_StagesLifecycleWithoutPersistence()
    {
        var batch = new FakeProjectionBatch();
        var applicator = new AgenticCommerceReconciliationApplicator();
        var chain = new ChainDefinition { Key = "test-chain", EvmChainId = 11155111 };

        await applicator.ApplyEventAsync(batch, chain, "0xEscrow", new EscrowEvent
        {
            Type = EscrowEventType.JobCreated,
            OnChainJobId = 42,
            Client = "0xClient",
            TransactionHash = "0xCreate",
            LogIndex = 0
        }, CancellationToken.None);

        await applicator.ApplyEventAsync(batch, chain, "0xEscrow", new EscrowEvent
        {
            Type = EscrowEventType.JobFunded,
            OnChainJobId = 42,
            Amount = 2_000_000_000_000_000_000,
            TransactionHash = "0xFund",
            LogIndex = 0
        }, CancellationToken.None);

        batch.Jobs.Should().ContainSingle();
        batch.Jobs[0].Status.Should().Be(AgenticJobProjection.StatusFunded);
        batch.Jobs[0].Budget.Should().Be(2m);
        batch.AppliedEvents.Should().HaveCount(2);
    }

    private sealed class FakeProjectionBatch : IAgenticCommerceProjectionBatch
    {
        public List<AgenticJobProjection> Jobs { get; } = [];
        public List<AgentDirectoryEntry> DirectoryEntries { get; } = [];
        public List<AgenticJobAppliedEvent> AppliedEvents { get; } = [];
        public List<AgenticJobDeferredEvent> DeferredEvents { get; } = [];

        public Task<bool> HasAppliedEventAsync(string chainKey, string contractAddress, string transactionHash, int logIndex, CancellationToken cancellationToken) =>
            Task.FromResult(AppliedEvents.Any(entry =>
                entry.ChainKey == chainKey && entry.ContractAddress == contractAddress
                && entry.TransactionHash == transactionHash && entry.LogIndex == logIndex));

        public Task<bool> HasDeferredEventAsync(string chainKey, string contractAddress, string transactionHash, int logIndex, CancellationToken cancellationToken) =>
            Task.FromResult(DeferredEvents.Any(entry =>
                entry.ChainKey == chainKey && entry.ContractAddress == contractAddress
                && entry.TransactionHash == transactionHash && entry.LogIndex == logIndex));

        public Task<AgenticJobProjection?> FindJobAsync(string chainKey, string contractAddress, long onChainJobId, CancellationToken cancellationToken) =>
            Task.FromResult(Jobs.SingleOrDefault(job =>
                job.ChainKey == chainKey && job.ContractAddress == contractAddress && job.OnChainJobId == onChainJobId));

        public Task<bool> HasAgentDirectoryEntryAsync(string chainKey, string registryAddress, long agentId, CancellationToken cancellationToken) =>
            Task.FromResult(DirectoryEntries.Any(entry =>
                entry.ChainKey == chainKey && entry.RegistryAddress == registryAddress && entry.AgentId == agentId));

        public void StageJob(AgenticJobProjection job) => Jobs.Add(job);
        public void StageAgentDirectoryEntry(AgentDirectoryEntry entry) => DirectoryEntries.Add(entry);
        public void StageAppliedEvent(AgenticJobAppliedEvent appliedEvent) => AppliedEvents.Add(appliedEvent);
        public void StageDeferredEvent(AgenticJobDeferredEvent deferredEvent) => DeferredEvents.Add(deferredEvent);
    }
}
