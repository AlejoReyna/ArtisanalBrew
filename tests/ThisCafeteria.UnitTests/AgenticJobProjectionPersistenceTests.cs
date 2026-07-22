using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;
using Xunit;

namespace ThisCafeteria.UnitTests;

public class AgenticJobProjectionPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;

    public AgenticJobProjectionPersistenceTests()
    {
        // Use in-memory SQLite to test EF Core unique constraints
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task DuplicateOnChainIdentity_ThrowsUniqueConstraintException()
    {
        var job1 = new AgenticJobProjection
        {
            Id = Guid.NewGuid(),
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

        var job2 = new AgenticJobProjection
        {
            Id = Guid.NewGuid(),
            ChainKey = "ethereum-sepolia",
            ContractAddress = "0xEscrow",
            OnChainJobId = 42, // Duplicate!
            JobId = 2,
            ClientAddress = "0xClient2",
            ProviderAddress = "0xProvider2",
            EvaluatorAddress = "0xEvaluator2",
            DescriptionCommitment = "hash2",
            Status = AgenticJobProjection.StatusOpen
        };

        _context.AgenticJobs.Add(job1);
        await _context.SaveChangesAsync();

        _context.AgenticJobs.Add(job2);

        // Assert that the second save fails due to the unique constraint on (ChainKey, ContractAddress, OnChainJobId)
        await FluentActions.Invoking(() => _context.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task DuplicateJobIdAcrossChains_Succeeds()
    {
        var job1 = new AgenticJobProjection
        {
            Id = Guid.NewGuid(),
            ChainKey = "ethereum-sepolia",
            ContractAddress = "0xEscrow1",
            OnChainJobId = 42,
            JobId = 1, // Duplicate JobId
            ClientAddress = "0xClient",
            ProviderAddress = "0xProvider",
            EvaluatorAddress = "0xEvaluator",
            DescriptionCommitment = "hash1",
            Status = AgenticJobProjection.StatusOpen
        };

        var job2 = new AgenticJobProjection
        {
            Id = Guid.NewGuid(),
            ChainKey = "arbitrum-sepolia",
            ContractAddress = "0xEscrow2",
            OnChainJobId = 42,
            JobId = 1, // Duplicate JobId
            ClientAddress = "0xClient2",
            ProviderAddress = "0xProvider2",
            EvaluatorAddress = "0xEvaluator2",
            DescriptionCommitment = "hash2",
            Status = AgenticJobProjection.StatusOpen
        };

        _context.AgenticJobs.Add(job1);
        _context.AgenticJobs.Add(job2);

        // This should succeed because the unique constraint is no longer on (ChainKey, JobId)
        await _context.SaveChangesAsync();

        var savedJobs = await _context.AgenticJobs.ToListAsync();
        savedJobs.Should().HaveCount(2);
    }
}
