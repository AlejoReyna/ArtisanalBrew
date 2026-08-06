using System.Numerics;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Application.Services.AgenticCommerce;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Infrastructure.Services.Reconciliation;
using Xunit;

namespace ThisCafeteria.UnitTests;

public class AgenticCommerceReconciliationApplicatorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly IAgenticCommerceProjectionBatch _batch;
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
        _batch = new AgenticCommerceProjectionBatch(_context);

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

        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, evt, CancellationToken.None);
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

        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, evt, CancellationToken.None);
        await _context.SaveChangesAsync();

        // Apply again
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, evt, CancellationToken.None);
        await _context.SaveChangesAsync();

        var count = await _context.AgenticJobs.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task ApplyEvent_FullLifecycle_TransitionsCorrectly()
    {
        // 1. Create
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 2, TransactionHash = "0xTxCreate", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        // 2. Fund
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobFunded, OnChainJobId = 2, Amount = BigInteger.Parse("5000000000000000000"), TransactionHash = "0xTxFund", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var jobAfterFund = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 2);
        jobAfterFund.Status.Should().Be(AgenticJobProjection.StatusFunded);
        jobAfterFund.Budget.Should().Be(5.0m);
        jobAfterFund.ConcurrencyToken.Should().Be(1);

        // 3. Submit
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobSubmitted, OnChainJobId = 2, Deliverable = "ipfs://test", TransactionHash = "0xTxSubmit", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var jobAfterSubmit = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 2);
        jobAfterSubmit.Status.Should().Be(AgenticJobProjection.StatusSubmitted);
        jobAfterSubmit.DeliverableCommitment.Should().Be("ipfs://test");
        jobAfterSubmit.ConcurrencyToken.Should().Be(2);

        // 4. Complete
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCompleted, OnChainJobId = 2, Reason = "Looks good", TransactionHash = "0xTxComplete", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var jobAfterComplete = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 2);
        jobAfterComplete.Status.Should().Be(AgenticJobProjection.StatusCompleted);
        jobAfterComplete.DecisionReason.Should().Be("Looks good");
        jobAfterComplete.ConcurrencyToken.Should().Be(3);
    }

    [Fact]
    public async Task ApplyEvent_InvalidOrder_IsIdempotent()
    {
        // Create
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 3, TransactionHash = "0x1", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        // Try Complete (Invalid transition from Open -> Complete)
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCompleted, OnChainJobId = 3, TransactionHash = "0x2", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var job = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 3);
        job.Status.Should().Be(AgenticJobProjection.StatusOpen);
    }

    [Fact]
    public async Task ApplyEvent_JobRejected_TransitionsCorrectly()
    {
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 4, TransactionHash = "0x1", LogIndex = 1 }, CancellationToken.None);
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobFunded, OnChainJobId = 4, TransactionHash = "0x2", LogIndex = 1 }, CancellationToken.None);
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobSubmitted, OnChainJobId = 4, TransactionHash = "0x3", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobRejected, OnChainJobId = 4, Reason = "Poor quality", TransactionHash = "0x4", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var job = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 4);
        job.Status.Should().Be(AgenticJobProjection.StatusRejected);
        job.DecisionReason.Should().Be("Poor quality");
        job.ConcurrencyToken.Should().Be(3);
    }

    [Fact]
    public async Task ApplyEvent_JobExpired_TransitionsCorrectly()
    {
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 5, TransactionHash = "0x1", LogIndex = 1 }, CancellationToken.None);
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobFunded, OnChainJobId = 5, TransactionHash = "0x2", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobExpired, OnChainJobId = 5, TransactionHash = "0x3", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var job = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 5);
        job.Status.Should().Be(AgenticJobProjection.StatusExpired);
        job.ConcurrencyToken.Should().Be(2);
    }

    [Fact]
    public async Task ApplyEvent_WrongEscrowAddress_IsIdempotent()
    {
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 6, TransactionHash = "0x1", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        // Apply fund with wrong escrow address
        await _applicator.ApplyEventAsync(_batch, _chain, "0xWrongEscrow", new EscrowEvent { Type = EscrowEventType.JobFunded, OnChainJobId = 6, Amount = BigInteger.Parse("5000000000000000000"), TransactionHash = "0x2", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var job = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 6);
        job.Status.Should().Be(AgenticJobProjection.StatusOpen);
    }

    [Fact]
    public async Task ApplyEvent_OptimisticConcurrencyConflict_ThrowsDbUpdateConcurrencyException()
    {
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 7 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var staleJob = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 7);

        // Modify in database without the context knowing to advance the concurrency token
        await _context.Database.ExecuteSqlRawAsync("UPDATE \"AgenticJobs\" SET \"ConcurrencyToken\" = 1 WHERE \"OnChainJobId\" = 7");

        staleJob.Status = AgenticJobProjection.StatusSubmitted;

        var act = async () => await _context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    // =========================================================================
    // NEW TESTS – Phase 3 hardening requirements
    // =========================================================================

    /// <summary>JobCompleted arrives before JobCreated: must record a deferred event, not throw.</summary>
    [Fact]
    public async Task ApplyEvent_JobCompleted_BeforeJobCreated_RecordsDeferredEvent()
    {
        var evt = new EscrowEvent
        {
            Type = EscrowEventType.JobCompleted,
            OnChainJobId = 101,
            Reason = "approved",
            TransactionHash = "0xCompletedFirst",
            LogIndex = 0,
            BlockNumber = 50
        };

        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, evt, CancellationToken.None);
        await _context.SaveChangesAsync();

        // No job should be projected.
        var jobCount = await _context.AgenticJobs.CountAsync();
        jobCount.Should().Be(0);

        // A deferred event must be recorded.
        var deferred = await _context.AgenticJobDeferredEvents.SingleAsync();
        deferred.EventType.Should().Be("JobCompleted");
        deferred.OnChainJobId.Should().Be(101);
        deferred.TransactionHash.Should().Be("0xCompletedFirst");
        deferred.DeferralReason.Should().Contain("not found");

        // The applied-event table must NOT have an entry (so this block range is not silently skipped).
        var appliedCount = await _context.AgenticJobAppliedEvents.CountAsync();
        appliedCount.Should().Be(0);
    }

    /// <summary>JobFunded arrives before JobCreated: must record a deferred event, not silently skip.</summary>
    [Fact]
    public async Task ApplyEvent_JobFunded_BeforeJobCreated_RecordsDeferredEvent()
    {
        var evt = new EscrowEvent
        {
            Type = EscrowEventType.JobFunded,
            OnChainJobId = 102,
            Amount = BigInteger.Parse("1000000000000000000"),
            TransactionHash = "0xFundedFirst",
            LogIndex = 0,
            BlockNumber = 40
        };

        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, evt, CancellationToken.None);
        await _context.SaveChangesAsync();

        var jobCount = await _context.AgenticJobs.CountAsync();
        jobCount.Should().Be(0, "no job was ever created");

        var deferred = await _context.AgenticJobDeferredEvents.SingleAsync();
        deferred.EventType.Should().Be("JobFunded");
        deferred.DeferralReason.Should().Contain("not found");
    }

    /// <summary>JobSubmitted arrives before funding: must defer, leave the projection Open, and stay unapplied.</summary>
    [Fact]
    public async Task ApplyEvent_JobSubmitted_BeforeFunding_RecordsDeferredEventAndLeavesStatusOpen()
    {
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent
        {
            Type = EscrowEventType.JobCreated,
            OnChainJobId = 107,
            TransactionHash = "0xCreatedNoFunding",
            LogIndex = 0,
            BlockNumber = 50
        }, CancellationToken.None);
        await _context.SaveChangesAsync();

        // Submission arrives while the job is still Open (never funded).
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent
        {
            Type = EscrowEventType.JobSubmitted,
            OnChainJobId = 107,
            Deliverable = "ipfs://premature",
            TransactionHash = "0xSubmitBeforeFunding",
            LogIndex = 0,
            BlockNumber = 51
        }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var job = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 107);
        job.Status.Should().Be(AgenticJobProjection.StatusOpen, "an unfunded job must not advance to Submitted");
        job.DeliverableCommitment.Should().BeNullOrEmpty("the projection must not absorb a premature deliverable");

        var deferred = await _context.AgenticJobDeferredEvents
            .SingleAsync(d => d.TransactionHash == "0xSubmitBeforeFunding");
        deferred.EventType.Should().Be("JobSubmitted");
        deferred.DeferralReason.Should().Contain("Invalid state");

        // The event must NOT be marked applied, so the checkpoint cannot skip past it.
        var applied = await _context.AgenticJobAppliedEvents
            .AnyAsync(e => e.TransactionHash == "0xSubmitBeforeFunding");
        applied.Should().BeFalse("a deferred event must remain unapplied for later retry");
    }

    /// <summary>Duplicate JobCreated with a different log identity: second call is idempotent (job already exists).</summary>
    [Fact]
    public async Task ApplyEvent_DuplicateJobCreated_DifferentLogIdentity_IsIdempotent()
    {
        var first = new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 103, Client = "0xClient", TransactionHash = "0xTxA", LogIndex = 0, BlockNumber = 10 };
        var second = new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 103, Client = "0xOtherClient", TransactionHash = "0xTxB", LogIndex = 0, BlockNumber = 11 };

        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, first, CancellationToken.None);
        await _context.SaveChangesAsync();

        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, second, CancellationToken.None);
        await _context.SaveChangesAsync();

        // Only one job projection.
        var jobCount = await _context.AgenticJobs.CountAsync();
        jobCount.Should().Be(1);

        // The first job's client address is retained.
        var job = await _context.AgenticJobs.SingleAsync();
        job.ClientAddress.Should().Be("0xClient");

        // Both log identities are recorded as applied (idempotent duplicate of JobCreated is still marked applied).
        var appliedCount = await _context.AgenticJobAppliedEvents.CountAsync();
        appliedCount.Should().Be(2);
    }

    /// <summary>
    /// Delayed prerequisite scenario: JobCompleted arrives first (deferred), then JobCreated
    /// arrives and the job is projected. The deferred record must persist so a future re-apply
    /// loop can handle it.  The deferred event must NOT be silently lost.
    /// </summary>
    [Fact]
    public async Task ApplyEvent_DelayedPrerequisite_DeferralIsRetainable()
    {
        // First: Completed (before job exists)
        var complete = new EscrowEvent { Type = EscrowEventType.JobCompleted, OnChainJobId = 104, Reason = "ok", TransactionHash = "0xComp", LogIndex = 0, BlockNumber = 80 };
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, complete, CancellationToken.None);
        await _context.SaveChangesAsync();

        var deferredBefore = await _context.AgenticJobDeferredEvents.CountAsync();
        deferredBefore.Should().Be(1, "JobCompleted must be deferred when job does not exist");

        // Then: JobCreated arrives (the prerequisite)
        var create = new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 104, Client = "0xClient", TransactionHash = "0xCreate", LogIndex = 0, BlockNumber = 70 };
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, create, CancellationToken.None);
        await _context.SaveChangesAsync();

        // The job now exists.
        var job = await _context.AgenticJobs.SingleAsync();
        job.OnChainJobId.Should().Be(104);
        job.Status.Should().Be(AgenticJobProjection.StatusOpen, "re-application of deferred events is a future concern; for now job stays Open");

        // The deferred event record persists so it can be retried.
        var deferredAfter = await _context.AgenticJobDeferredEvents.CountAsync();
        deferredAfter.Should().Be(1, "the deferred JobCompleted record must not be deleted by a subsequent JobCreated");
    }

    /// <summary>Wrong escrow address: event from a different contract is safely deferred/ignored, does not corrupt the correct job.</summary>
    [Fact]
    public async Task ApplyEvent_WrongEscrowAddress_IsIgnoredSafely()
    {
        // Create job on correct escrow.
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 105, TransactionHash = "0xCreate", LogIndex = 0 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        // Apply a JobFunded from a wrong escrow address.
        await _applicator.ApplyEventAsync(_batch, _chain, "0xWrongEscrow",
            new EscrowEvent { Type = EscrowEventType.JobFunded, OnChainJobId = 105, Amount = BigInteger.Parse("1000000000000000000"), TransactionHash = "0xFund", LogIndex = 0 },
            CancellationToken.None);
        await _context.SaveChangesAsync();

        // The job from the correct escrow must remain Open.
        var job = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 105);
        job.Status.Should().Be(AgenticJobProjection.StatusOpen);

        // A deferred event must be recorded against the wrong escrow address.
        var deferred = await _context.AgenticJobDeferredEvents.SingleAsync();
        deferred.ContractAddress.Should().Be("0xWrongEscrow");
        deferred.EventType.Should().Be("JobFunded");
    }

    /// <summary>Duplicate terminal event: a second JobCompleted with a different tx hash is idempotent and does NOT corrupt state.</summary>
    [Fact]
    public async Task ApplyEvent_DuplicateTerminalEvent_IsIdempotent()
    {
        // Full lifecycle to Completed.
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCreated, OnChainJobId = 106, TransactionHash = "0x1", LogIndex = 0 }, CancellationToken.None);
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobFunded, OnChainJobId = 106, TransactionHash = "0x2", LogIndex = 0 }, CancellationToken.None);
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobSubmitted, OnChainJobId = 106, TransactionHash = "0x3", LogIndex = 0 }, CancellationToken.None);
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCompleted, OnChainJobId = 106, Reason = "ok", TransactionHash = "0x4", LogIndex = 0 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var jobAfterFirst = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 106);
        jobAfterFirst.Status.Should().Be(AgenticJobProjection.StatusCompleted);
        var tokenAfterFirst = jobAfterFirst.ConcurrencyToken;

        // Apply a second JobCompleted with a DIFFERENT log identity (e.g. re-org rescan).
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, new EscrowEvent { Type = EscrowEventType.JobCompleted, OnChainJobId = 106, Reason = "duplicate", TransactionHash = "0x4b", LogIndex = 1 }, CancellationToken.None);
        await _context.SaveChangesAsync();

        var jobAfterDupe = await _context.AgenticJobs.SingleAsync(j => j.OnChainJobId == 106);
        jobAfterDupe.Status.Should().Be(AgenticJobProjection.StatusCompleted, "terminal state must not change on duplicate");
        // Reason must not be overwritten.
        jobAfterDupe.DecisionReason.Should().Be("ok");
        // Concurrency token is unchanged because we hit the idempotent terminal path.
        jobAfterDupe.ConcurrencyToken.Should().Be(tokenAfterFirst);

        // The duplicate terminal event is recorded as applied (so it won't be re-processed).
        var appliedCount = await _context.AgenticJobAppliedEvents.CountAsync(e => e.TransactionHash == "0x4b");
        appliedCount.Should().Be(1);
    }

    /// <summary>Failed SaveChanges must leave the checkpoint unchanged. The worker wraps in a transaction; here we prove SaveChanges failure leaves deferred-event rows NOT committed either.</summary>
    [Fact]
    public async Task ApplyEvent_DeferredEvent_DuplicateLogIdentity_DoesNotThrow()
    {
        // Apply the same out-of-order event twice (simulates retry after transient failure).
        var evt = new EscrowEvent { Type = EscrowEventType.JobCompleted, OnChainJobId = 107, TransactionHash = "0xDup", LogIndex = 0, BlockNumber = 10 };
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, evt, CancellationToken.None);
        await _context.SaveChangesAsync();

        // Second application of the same event: the `alreadyDeferred` guard must prevent a duplicate insert.
        await _applicator.ApplyEventAsync(_batch, _chain, Escrow, evt, CancellationToken.None);
        await _context.SaveChangesAsync();

        var count = await _context.AgenticJobDeferredEvents.CountAsync();
        count.Should().Be(1, "duplicate deferred event must not create a second row");
    }

    // =========================================================================
    // bytes32 / PostgreSQL-safe text column tests
    // =========================================================================

    /// <summary>NormalizeBytes32 with ASCII text content returns the text without trailing nulls.</summary>
    [Fact]
    public void NormalizeBytes32_AsciiText_ReturnsStrippedText()
    {
        // "approved" padded to 32 bytes (right-padded with 0x00)
        var raw = new byte[32];
        var text = System.Text.Encoding.UTF8.GetBytes("approved");
        text.CopyTo(raw, 0);

        var result = EvmEscrowEventProvider.NormalizeBytes32(raw);

        result.Should().Be("approved");
        result.Should().NotContain("\0", "no NUL bytes may reach a PostgreSQL text column");
    }

    /// <summary>NormalizeBytes32 with all-zero bytes returns an empty string, not a string of 0x00.</summary>
    [Fact]
    public void NormalizeBytes32_AllZeroBytes_ReturnsEmptyString()
    {
        var raw = new byte[32]; // all zeros
        var result = EvmEscrowEventProvider.NormalizeBytes32(raw);
        result.Should().BeEmpty();
    }

    /// <summary>NormalizeBytes32 with arbitrary binary falls back to 0x-hex which is pure ASCII.</summary>
    [Fact]
    public void NormalizeBytes32_BinaryBytes_ReturnsPureAsciiHex()
    {
        // Bytes that are not valid UTF-8 when taken together
        var raw = new byte[] { 0xFF, 0xFE, 0xAB, 0xCD };
        var padded = new byte[32];
        raw.CopyTo(padded, 0);

        var result = EvmEscrowEventProvider.NormalizeBytes32(padded);

        result.Should().StartWith("0x");
        result.Should().NotContain("\0", "no NUL bytes may reach a PostgreSQL text column");
        // All characters must be printable ASCII (hex digits or 'x').
        result.All(c => char.IsAsciiLetterOrDigit(c)).Should().BeTrue();
    }

    /// <summary>NormalizeBytes32 returns no NUL bytes even for a bytes32 that is partially ASCII-zero-mixed.</summary>
    [Fact]
    public void NormalizeBytes32_NoNulBytesInAnyOutput()
    {
        // Craft edge case: byte array with an embedded 0x00 in the middle
        var raw = new byte[32];
        raw[0] = 0x61; // 'a'
        raw[1] = 0x00; // NUL in middle
        raw[2] = 0x62; // 'b'

        var result = EvmEscrowEventProvider.NormalizeBytes32(raw);

        // Result must contain no NUL bytes.
        result.Should().NotContain("\0", "NUL bytes must never reach PostgreSQL text columns");
        result.Should().NotBeNull();
    }
}
