using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Application.Services.AgenticCommerce;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Infrastructure.Services.Reconciliation;
using Xunit;

namespace ThisCafeteria.UnitTests;

public class AgenticCommerceReconciliationWorkerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Mock<IChainRegistry> _registryMock;
    private readonly Mock<IEscrowEventProvider> _providerMock;
    private readonly AgenticCommerceReconciliationApplicator _applicator;
    private readonly AgenticCommerceReconciliationWorker _worker;
    private readonly ChainDefinition _chain;

    public AgenticCommerceReconciliationWorkerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(options));
        services.AddScoped<IAgenticCommerceProjectionBatch, AgenticCommerceProjectionBatch>();
        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        _chain = new ChainDefinition
        {
            Key = "test-chain",
            Family = ChainFamily.Evm,
            Enabled = true,
            Capabilities = new ChainCapabilities { AgenticCommerce = true },
            Deployment = new ChainDeployment { AgenticEscrow = "0xEscrow" },
            MinimumConfirmations = 5
        };

        _registryMock = new Mock<IChainRegistry>();
        _registryMock.Setup(r => r.All).Returns(new[] { _chain });

        _providerMock = new Mock<IEscrowEventProvider>();

        _applicator = new AgenticCommerceReconciliationApplicator();

        _worker = new AgenticCommerceReconciliationWorker(
            _scopeFactory,
            _registryMock.Object,
            _providerMock.Object,
            _applicator,
            NullLogger<AgenticCommerceReconciliationWorker>.Instance
        );
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ReconcileOnceAsync_CreatesProjectionAndAdvancesCheckpoint()
    {
        // Arrange
        // safeHead = latest - 5 = 100
        _providerMock.Setup(p => p.GetLatestBlockNumberAsync(_chain, It.IsAny<CancellationToken>()))
            .ReturnsAsync(105);

        // Events returned for fromBlock=0 to toBlock=100
        var jobCreatedEvent = new EscrowEvent
        {
            Type = EscrowEventType.JobCreated,
            OnChainJobId = 1,
            Client = "0xClient",
            Provider = "0xProvider",
            Evaluator = "0xEvaluator",
            BlockNumber = 50,
            LogIndex = 1
        };

        _providerMock.Setup(p => p.DecodeEventsAsync(_chain, "0xEscrow", It.IsAny<long>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EscrowEvent> { jobCreatedEvent });

        // Act
        await _worker.ReconcileOnceAsync(_chain, CancellationToken.None);

        // Assert Checkpoint
        var checkpoint = await _context.AgenticCommerceCheckpoints.SingleAsync(c => c.ChainKey == "test-chain");
        checkpoint.LastScannedBlock.Should().Be(100);

        // Assert Projection
        var projection = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 1);
        projection.Status.Should().Be(AgenticJobProjection.StatusOpen);
        projection.ClientAddress.Should().Be("0xClient");

        // Assert Applicator processed it correctly (concurrency token = 0 on creation)
        projection.ConcurrencyToken.Should().Be(0);
    }

    [Fact]
    public async Task ReconcileOnceAsync_RpcFailureLeavesCheckpointUnchanged()
    {
        // Arrange
        _providerMock.Setup(p => p.GetLatestBlockNumberAsync(_chain, It.IsAny<CancellationToken>()))
            .ReturnsAsync(105);

        // Setup checkpoint at block 10
        _context.AgenticCommerceCheckpoints.Add(new AgenticCommerceReconciliationCheckpoint
        {
            ChainKey = "test-chain",
            EscrowAddress = "0xEscrow",
            LastScannedBlock = 10
        });
        await _context.SaveChangesAsync();

        // Simulate RPC failure during event fetching
        _providerMock.Setup(p => p.DecodeEventsAsync(_chain, "0xEscrow", 11, 100, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RPC Failed"));

        // Act & Assert
        await FluentActions.Invoking(() => _worker.ReconcileOnceAsync(_chain, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("RPC Failed");

        // Assert Checkpoint remains at 10
        var checkpoint = await _context.AgenticCommerceCheckpoints.SingleAsync(c => c.ChainKey == "test-chain");
        checkpoint.LastScannedBlock.Should().Be(10);
    }

    [Fact]
    public async Task ReconcileOnceAsync_MultipleJobsInOneScan()
    {
        _providerMock.Setup(p => p.GetLatestBlockNumberAsync(_chain, It.IsAny<CancellationToken>()))
            .ReturnsAsync(105);

        var events = new List<EscrowEvent>
        {
            new() { Type = EscrowEventType.JobCreated, OnChainJobId = 1, BlockNumber = 10, LogIndex = 0 },
            new() { Type = EscrowEventType.JobFunded, OnChainJobId = 1, BlockNumber = 10, LogIndex = 1 },
            new() { Type = EscrowEventType.JobCreated, OnChainJobId = 2, BlockNumber = 12, LogIndex = 0 }
        };

        _providerMock.Setup(p => p.DecodeEventsAsync(_chain, "0xEscrow", It.IsAny<long>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);

        await _worker.ReconcileOnceAsync(_chain, CancellationToken.None);

        var jobs = await _context.AgenticJobs.OrderBy(j => j.OnChainJobId).ToListAsync();
        jobs.Should().HaveCount(2);
        jobs[0].OnChainJobId.Should().Be(1);
        jobs[0].Status.Should().Be(AgenticJobProjection.StatusFunded);
        jobs[1].OnChainJobId.Should().Be(2);
        jobs[1].Status.Should().Be(AgenticJobProjection.StatusOpen);
    }

    [Fact]
    public async Task ReconcileOnceAsync_ApplicatorFailureLeavesCheckpointUnchanged()
    {
        _providerMock.Setup(p => p.GetLatestBlockNumberAsync(_chain, It.IsAny<CancellationToken>()))
            .ReturnsAsync(105);

        _context.AgenticCommerceCheckpoints.Add(new AgenticCommerceReconciliationCheckpoint
        {
            ChainKey = "test-chain",
            EscrowAddress = "0xEscrow",
            LastScannedBlock = 10
        });
        await _context.SaveChangesAsync();

        var evt = new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 3, BlockNumber = 50 };
        _providerMock.Setup(p => p.DecodeEventsAsync(_chain, "0xEscrow", 11, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EscrowEvent> { evt });

        var failingApplicator = new Mock<IAgenticCommerceReconciliationApplicator>();
        failingApplicator.Setup(a => a.ApplyEventAsync(It.IsAny<IAgenticCommerceProjectionBatch>(), _chain, "0xEscrow", evt, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Database error"));

        var workerWithFailingApplicator = new AgenticCommerceReconciliationWorker(
            _scopeFactory, _registryMock.Object, _providerMock.Object, failingApplicator.Object, NullLogger<AgenticCommerceReconciliationWorker>.Instance);

        await FluentActions.Invoking(() => workerWithFailingApplicator.ReconcileOnceAsync(_chain, CancellationToken.None))
            .Should().ThrowAsync<DbUpdateException>();

        var checkpoint = await _context.AgenticCommerceCheckpoints.SingleAsync(c => c.ChainKey == "test-chain");
        checkpoint.LastScannedBlock.Should().Be(10, "Checkpoint should not advance if applicator throws");
    }

    // =========================================================================
    // NEW TEST – Phase 3 hardening: failed persistence leaves checkpoint unchanged
    // =========================================================================

    /// <summary>
    /// If the applicator throws a DbUpdateException (persistence failure), the transaction
    /// is rolled back and the checkpoint must remain at its previous value.
    ///
    /// This is distinct from ReconcileOnceAsync_ApplicatorFailureLeavesCheckpointUnchanged:
    /// here we use a checkpoint at block 20 and a higher latest block (130) to confirm
    /// the specific arithmetic: the checkpoint does not advance from 20 to any higher value.
    /// </summary>
    [Fact]
    public async Task ReconcileOnceAsync_PersistenceFailure_CheckpointRemainsAtPreviousBlock()
    {
        // Arrange: checkpoint at block 20, latest at 130 → safeHead = 125.
        _providerMock.Setup(p => p.GetLatestBlockNumberAsync(_chain, It.IsAny<CancellationToken>()))
            .ReturnsAsync(130);

        _context.AgenticCommerceCheckpoints.Add(new AgenticCommerceReconciliationCheckpoint
        {
            ChainKey = "test-chain",
            EscrowAddress = "0xEscrow",
            LastScannedBlock = 20
        });
        await _context.SaveChangesAsync();

        var evt = new EscrowEvent
        {
            Type = EscrowEventType.JobCreated,
            OnChainJobId = 200,
            BlockNumber = 25,
            TransactionHash = "0xPersistFail",
            LogIndex = 0
        };
        _providerMock
            .Setup(p => p.DecodeEventsAsync(_chain, "0xEscrow", 21, 125, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EscrowEvent> { evt });

        // The applicator throws a DbUpdateException, simulating a failed SaveChanges.
        var failApplicator = new Mock<IAgenticCommerceReconciliationApplicator>();
        failApplicator
            .Setup(a => a.ApplyEventAsync(
                It.IsAny<IAgenticCommerceProjectionBatch>(), _chain, "0xEscrow", evt, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Persistence failure"));

        var workerFail = new AgenticCommerceReconciliationWorker(
            _scopeFactory, _registryMock.Object, _providerMock.Object, failApplicator.Object,
            NullLogger<AgenticCommerceReconciliationWorker>.Instance);

        // Act.
        await FluentActions.Invoking(() => workerFail.ReconcileOnceAsync(_chain, CancellationToken.None))
            .Should().ThrowAsync<DbUpdateException>();

        // Assert: checkpoint must still be at 20, not 125.
        var checkpoint = await _context.AgenticCommerceCheckpoints.AsNoTracking()
            .SingleAsync(c => c.ChainKey == "test-chain");
        checkpoint.LastScannedBlock.Should().Be(20,
            "checkpoint must not advance when persistence fails at any point in the transaction");
    }
}
