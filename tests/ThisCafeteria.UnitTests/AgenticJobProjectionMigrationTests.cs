using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;
using Xunit;

namespace ThisCafeteria.UnitTests;

public class AgenticJobProjectionMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AgenticJobProjectionMigrationTests()
    {
        // Use an in-memory database instance that remains open
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task MigrateAsync_AppliesUniqueConstraintSuccessfully()
    {
        using var setupContext = CreateContext();

        // 1. Actually run migrations rather than EnsureCreated()
        await setupContext.Database.MigrateAsync();

        // 2. Insert two records with the same (ChainKey, JobId) but DIFFERENT ContractAddresses/OnChainJobIds
        // (Old constraint would fail this)
        var job1 = new AgenticJobProjection
        {
            ChainKey = "ethereum-sepolia",
            ContractAddress = "0xEscrow1",
            OnChainJobId = 1,
            JobId = 42,
            ClientAddress = "0xClient",
            ProviderAddress = "0xProvider",
            EvaluatorAddress = "0xEvaluator",
            DescriptionCommitment = "hash1",
            Status = AgenticJobProjection.StatusOpen
        };

        var job2 = new AgenticJobProjection
        {
            ChainKey = "ethereum-sepolia",
            ContractAddress = "0xEscrow2",
            OnChainJobId = 2,
            JobId = 42, // Duplicate JobId
            ClientAddress = "0xClient",
            ProviderAddress = "0xProvider",
            EvaluatorAddress = "0xEvaluator",
            DescriptionCommitment = "hash1",
            Status = AgenticJobProjection.StatusOpen
        };

        setupContext.AgenticJobs.Add(job1);
        setupContext.AgenticJobs.Add(job2);

        await FluentActions.Invoking(() => setupContext.SaveChangesAsync())
            .Should().NotThrowAsync("the unique constraint on (ChainKey, JobId) was successfully removed by migrations");

        // 3. Insert a record that duplicates (ChainKey, ContractAddress, OnChainJobId)
        // (New constraint must fail this)
        var job3 = new AgenticJobProjection
        {
            ChainKey = "ethereum-sepolia",
            ContractAddress = "0xEscrow1",
            OnChainJobId = 1, // Duplicate OnChainJobId for this chain and contract!
            JobId = 99,
            ClientAddress = "0xClient",
            ProviderAddress = "0xProvider",
            EvaluatorAddress = "0xEvaluator",
            DescriptionCommitment = "hash1",
            Status = AgenticJobProjection.StatusOpen
        };

        setupContext.AgenticJobs.Add(job3);

        await FluentActions.Invoking(() => setupContext.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateException>("the migration successfully applied the unique index on (ChainKey, ContractAddress, OnChainJobId)");
    }
}
