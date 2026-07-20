using System.Numerics;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Worker;
using Xunit;

namespace ThisCafeteria.UnitTests;

public class AgenticCommerceReconciliationApplicatorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly AgenticCommerceReconciliationApplicator _applicator;
    private readonly ChainDefinition _chain;
    private const string Escrow = "0xEscrow";

    public AgenticCommerceReconciliationApplicatorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _applicator = new AgenticCommerceReconciliationApplicator();
        _chain = new ChainDefinition { Key = "ethereum-sepolia", EvmChainId = 11155111 };
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ApplyEvent_JobCreated_CreatesProjection()
    {
        var evt = new EscrowEvent
        {
            Type = EscrowEventType.JobCreated,
            OnChainJobId = 1,
            Client = "0xClient",
            Provider = "0xProvider",
            Evaluator = "0xEvaluator",
            TransactionHash = "0xTx1",
            BlockNumber = 100
        };

        await _applicator.ApplyEventAsync(_context, _chain, Escrow, evt, CancellationToken.None);
        await _context.SaveChangesAsync();

        var job = await _context.AgenticJobs.SingleAsync();
        job.ChainKey.Should().Be("ethereum-sepolia");
        job.OnChainJobId.Should().Be(1);
        job.Status.Should().Be(AgenticJobProjection.StatusOpen);
        job.CreationTransactionHash.Should().Be("0xTx1");
        job.ConcurrencyToken.Should().Be(0);
    }

    [Fact]
    public async Task ApplyEvent_JobCreated_Idempotent()
    {
        var evt = new EscrowEvent
        {
            Type = EscrowEventType.JobCreated,
            OnChainJobId = 1,
            TransactionHash = "0xTx1",
            LogIndex = 1
        };

        await _applicator.ApplyEventAsync(_context, _chain, Escrow, evt, CancellationToken.None);
        await _context.SaveChangesAsync();
        
        // Apply again
        await _applicator.ApplyEventAsync(_context, _chain, Escrow, evt, CancellationToken.None);
        await _context.SaveChangesAsync();

        var count = await _context.AgenticJobs.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task ApplyEvent_FullLifecycle_TransitionsCorrectly()
    {
        // 1. Create
        await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 2, TransactionHash = "0xTxCreate", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        // 2. Fund
        await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobFunded, OnChainJobId = 2, Amount = BigInteger.Parse("5000000000000000000"), TransactionHash = "0xTxFund", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var jobAfterFund = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 2);
        jobAfterFund.Status.Should().Be(AgenticJobProjection.StatusFunded);
        jobAfterFund.Budget.Should().Be(5.0m);
        jobAfterFund.ConcurrencyToken.Should().Be(1);

        // 3. Submit
        await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobSubmitted, OnChainJobId = 2, Deliverable = "ipfs://test", TransactionHash = "0xTxSubmit", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var jobAfterSubmit = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 2);
        jobAfterSubmit.Status.Should().Be(AgenticJobProjection.StatusSubmitted);
        jobAfterSubmit.DeliverableCommitment.Should().Be("ipfs://test");
        jobAfterSubmit.ConcurrencyToken.Should().Be(2);

        // 4. Complete
        await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCompleted, OnChainJobId = 2, Reason = "Looks good", TransactionHash = "0xTxComplete", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var jobAfterComplete = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 2);
        jobAfterComplete.Status.Should().Be(AgenticJobProjection.StatusCompleted);
        jobAfterComplete.DecisionReason.Should().Be("Looks good");
        jobAfterComplete.ConcurrencyToken.Should().Be(3);
    }

    [Fact]
    public async Task ApplyEvent_InvalidOrder_ThrowsException()
    {
        // Create
        await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 3, TransactionHash = "0x1", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        // Try Complete (Invalid transition from Open -> Complete)
        var act = async () => await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCompleted, OnChainJobId = 3, TransactionHash = "0x2", LogIndex = 1 }, CancellationToken.None);
        
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Invalid state for JobCompleted: Open");
    }

    [Fact]
    public async Task ApplyEvent_JobRejected_TransitionsCorrectly()
    {
        await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 4, TransactionHash = "0x1", LogIndex = 1 }, CancellationToken.None);
        await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobFunded, OnChainJobId = 4, TransactionHash = "0x2", LogIndex = 1 }, CancellationToken.None);
        await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobSubmitted, OnChainJobId = 4, TransactionHash = "0x3", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobRejected, OnChainJobId = 4, Reason = "Poor quality", TransactionHash = "0x4", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var job = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 4);
        job.Status.Should().Be(AgenticJobProjection.StatusRejected);
        job.DecisionReason.Should().Be("Poor quality");
        job.ConcurrencyToken.Should().Be(3);
    }

    [Fact]
    public async Task ApplyEvent_JobExpired_TransitionsCorrectly()
    {
        await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 5, TransactionHash = "0x1", LogIndex = 1 }, CancellationToken.None);
        await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobFunded, OnChainJobId = 5, TransactionHash = "0x2", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobExpired, OnChainJobId = 5, TransactionHash = "0x3", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var job = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 5);
        job.Status.Should().Be(AgenticJobProjection.StatusExpired);
        job.ConcurrencyToken.Should().Be(2);
    }

    [Fact]
    public async Task ApplyEvent_WrongEscrowAddress_ThrowsException()
    {
        await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 6, TransactionHash = "0x1", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        // Apply fund with wrong escrow address
        var act = async () => await _applicator.ApplyEventAsync(_context, _chain, "0xWrongEscrow", new EscrowEvent { Type = EscrowEventType.JobFunded, OnChainJobId = 6, Amount = BigInteger.Parse("5000000000000000000"), TransactionHash = "0x2", LogIndex = 1 }, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Job 6 not found");
    }

    [Fact]
    public async Task ApplyEvent_OptimisticConcurrencyConflict_ThrowsDbUpdateConcurrencyException()
    {
        await _applicator.ApplyEventAsync(_context, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 7 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var staleJob = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 7);

        // Modify in database without the context knowing to advance the concurrency token
        await _context.Database.ExecuteSqlRawAsync("UPDATE \"AgenticJobs\" SET \"ConcurrencyToken\" = 1 WHERE \"OnChainJobId\" = 7");

        staleJob.Status = AgenticJobProjection.StatusSubmitted;

        var act = async () => await _context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
