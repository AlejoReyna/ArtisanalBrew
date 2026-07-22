using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;
using Xunit;

namespace ThisCafeteria.UnitTests;

public class AgenticJobProjectionConcurrencyTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AgenticJobProjectionConcurrencyTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var setupContext = new AppDbContext(options);
        setupContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task ConcurrentUpdate_ThrowsDbUpdateConcurrencyException()
    {
        var jobId = Guid.NewGuid();

        // 1. Initial setup
        using (var setupContext = CreateContext())
        {
            var job = new AgenticJobProjection
            {
                Id = jobId,
                ChainKey = "ethereum-sepolia",
                ContractAddress = "0xEscrow",
                OnChainJobId = 42,
                JobId = 1,
                ClientAddress = "0xClient",
                ProviderAddress = "0xProvider",
                EvaluatorAddress = "0xEvaluator",
                DescriptionCommitment = "hash1",
                Status = AgenticJobProjection.StatusOpen
            };
            setupContext.AgenticJobs.Add(job);
            await setupContext.SaveChangesAsync();
        }

        // 2. Open two concurrent contexts
        using var context1 = CreateContext();
        using var context2 = CreateContext();

        // Load the same entity in both
        var jobInContext1 = await context1.AgenticJobs.SingleAsync(j => j.Id == jobId);
        var jobInContext2 = await context2.AgenticJobs.SingleAsync(j => j.Id == jobId);

        // Modify in Context 1 and save (simulate what AgenticJobService/Applicator does)
        jobInContext1.Status = AgenticJobProjection.StatusFunded;
        jobInContext1.ConcurrencyToken++;
        await context1.SaveChangesAsync();

        // Modify in Context 2
        jobInContext2.Status = AgenticJobProjection.StatusCompleted;
        jobInContext2.ConcurrencyToken++;

        // Save in Context 2 should fail because Context 1 bumped the ConcurrencyToken
        await FluentActions.Invoking(() => context2.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateConcurrencyException>();

        // Verify the database kept the first edit
        using var verifyContext = CreateContext();
        var verifiedJob = await verifyContext.AgenticJobs.SingleAsync(j => j.Id == jobId);
        verifiedJob.Status.Should().Be(AgenticJobProjection.StatusFunded, "the stale context failed to overwrite the newer projection");
    }
}
