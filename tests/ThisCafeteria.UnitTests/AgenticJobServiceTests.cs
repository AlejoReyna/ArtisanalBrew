using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Infrastructure.Services;
using Xunit;

namespace ThisCafeteria.UnitTests;

public class AgenticJobServiceTests
{
    private readonly AppDbContext _dbContext;
    private readonly Mock<IChainRegistry> _chainRegistryMock;
    private readonly AgenticJobService _service;

    public AgenticJobServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new AppDbContext(options);

        _chainRegistryMock = new Mock<IChainRegistry>();

        // Mock chain validation
        ChainDefinition dummyDef;
        _chainRegistryMock.Setup(x => x.TryGet(It.IsAny<string>(), out dummyDef)).Returns(true);
        _chainRegistryMock.Setup(x => x.TryGet("invalid-chain", out dummyDef)).Returns(false);

        _service = new AgenticJobService(_dbContext, _chainRegistryMock.Object);
    }

    [Fact]
    public async Task CreateJobAsync_WithValidInputs_CreatesJob()
    {
        var job = await _service.CreateJobAsync(
            "ethereum-sepolia",
            "0x1234567890123456789012345678901234567890",
            "0x2234567890123456789012345678901234567890",
            "0x3234567890123456789012345678901234567890",
            "commit123",
            100,
            DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds());

        job.Should().NotBeNull();
        job.Status.Should().Be(AgenticJobProjection.StatusOpen);
    }

    [Fact]
    public async Task CreateJobAsync_WithInvalidChain_ThrowsException()
    {
        await FluentActions.Invoking(() => _service.CreateJobAsync(
            "invalid-chain",
            "0x1234567890123456789012345678901234567890",
            "0x2234567890123456789012345678901234567890",
            "0x3234567890123456789012345678901234567890",
            "commit123",
            100,
            DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()))
            .Should().ThrowAsync<ArgumentException>().WithMessage("Unsupported chain key.");
    }

    [Fact]
    public async Task CreateJobAsync_WithInvalidAddress_ThrowsException()
    {
        await FluentActions.Invoking(() => _service.CreateJobAsync(
            "ethereum-sepolia",
            "invalid_address",
            "0x2234567890123456789012345678901234567890",
            "0x3234567890123456789012345678901234567890",
            "commit123",
            100,
            DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()))
            .Should().ThrowAsync<ArgumentException>().WithMessage("Invalid client address.");
    }

    [Fact]
    public async Task CreateJobAsync_WithNegativeBudget_ThrowsException()
    {
        await FluentActions.Invoking(() => _service.CreateJobAsync(
            "ethereum-sepolia",
            "0x1234567890123456789012345678901234567890",
            "0x2234567890123456789012345678901234567890",
            "0x3234567890123456789012345678901234567890",
            "commit123",
            -10,
            DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()))
            .Should().ThrowAsync<ArgumentException>().WithMessage("Budget must be greater than zero.");
    }

    [Fact]
    public async Task CreateJobAsync_WithPastExpiry_ThrowsException()
    {
        await FluentActions.Invoking(() => _service.CreateJobAsync(
            "ethereum-sepolia",
            "0x1234567890123456789012345678901234567890",
            "0x2234567890123456789012345678901234567890",
            "0x3234567890123456789012345678901234567890",
            "commit123",
            100,
            DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()))
            .Should().ThrowAsync<ArgumentException>().WithMessage("Expiry must be in the future.");
    }

    [Theory]
    [InlineData(AgenticJobProjection.StatusOpen, AgenticJobProjection.StatusFunded)]
    [InlineData(AgenticJobProjection.StatusOpen, AgenticJobProjection.StatusRejected)]
    [InlineData(AgenticJobProjection.StatusFunded, AgenticJobProjection.StatusSubmitted)]
    [InlineData(AgenticJobProjection.StatusFunded, AgenticJobProjection.StatusRejected)]
    [InlineData(AgenticJobProjection.StatusFunded, AgenticJobProjection.StatusExpired)]
    [InlineData(AgenticJobProjection.StatusSubmitted, AgenticJobProjection.StatusCompleted)]
    [InlineData(AgenticJobProjection.StatusSubmitted, AgenticJobProjection.StatusRejected)]
    [InlineData(AgenticJobProjection.StatusSubmitted, AgenticJobProjection.StatusExpired)]
    public async Task AdvanceJobStatusAsync_WithValidTransitions_Succeeds(string currentStatus, string newStatus)
    {
        var job = new AgenticJobProjection { Status = currentStatus };
        _dbContext.AgenticJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        await _service.AdvanceJobStatusAsync(job.Id, currentStatus, newStatus);

        var updatedJob = await _dbContext.AgenticJobs.FindAsync(job.Id);
        updatedJob!.Status.Should().Be(newStatus);
    }

    [Theory]
    [InlineData(AgenticJobProjection.StatusOpen, AgenticJobProjection.StatusCompleted)]
    [InlineData(AgenticJobProjection.StatusFunded, AgenticJobProjection.StatusOpen)]
    [InlineData(AgenticJobProjection.StatusSubmitted, AgenticJobProjection.StatusOpen)]
    public async Task AdvanceJobStatusAsync_WithInvalidTransitions_ThrowsException(string currentStatus, string newStatus)
    {
        var job = new AgenticJobProjection { Status = currentStatus };
        _dbContext.AgenticJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        await FluentActions.Invoking(() => _service.AdvanceJobStatusAsync(job.Id, currentStatus, newStatus))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Transition from {currentStatus} to {newStatus} is not allowed.");
    }

    [Theory]
    [InlineData(AgenticJobProjection.StatusCompleted, AgenticJobProjection.StatusOpen)]
    [InlineData(AgenticJobProjection.StatusRejected, AgenticJobProjection.StatusOpen)]
    [InlineData(AgenticJobProjection.StatusExpired, AgenticJobProjection.StatusOpen)]
    public async Task AdvanceJobStatusAsync_FromTerminalState_ThrowsException(string currentStatus, string newStatus)
    {
        var job = new AgenticJobProjection { Status = currentStatus };
        _dbContext.AgenticJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        await FluentActions.Invoking(() => _service.AdvanceJobStatusAsync(job.Id, currentStatus, newStatus))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Terminal states are immutable.");
    }

    [Fact]
    public async Task AdvanceJobStatusAsync_WildcardExpectedStatus_ThrowsException()
    {
        var job = new AgenticJobProjection { Status = AgenticJobProjection.StatusOpen };
        _dbContext.AgenticJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        await FluentActions.Invoking(() => _service.AdvanceJobStatusAsync(job.Id, "*", AgenticJobProjection.StatusFunded))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Wildcard transitions are not allowed.");
    }

    [Fact]
    public async Task AdvanceJobStatusAsync_NonExistentJob_ThrowsException()
    {
        var fakeId = Guid.NewGuid();
        await FluentActions.Invoking(() => _service.AdvanceJobStatusAsync(fakeId, AgenticJobProjection.StatusOpen, AgenticJobProjection.StatusFunded))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Job {fakeId} not found.");
    }

    [Fact]
    public async Task AdvanceJobStatusAsync_WrongExpectedStatus_ThrowsException()
    {
        var job = new AgenticJobProjection { Status = AgenticJobProjection.StatusOpen };
        _dbContext.AgenticJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        await FluentActions.Invoking(() => _service.AdvanceJobStatusAsync(job.Id, AgenticJobProjection.StatusFunded, AgenticJobProjection.StatusSubmitted))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Invalid state transition: Job {job.Id} is in status 'Open', expected 'Funded'.");
    }

    [Fact]
    public async Task AdvanceJobStatusAsync_UpdateActionCallback_IsInvoked()
    {
        var job = new AgenticJobProjection { Status = AgenticJobProjection.StatusOpen };
        _dbContext.AgenticJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        bool callbackInvoked = false;
        string? deliverableSet = null;
        await _service.AdvanceJobStatusAsync(job.Id, AgenticJobProjection.StatusOpen, AgenticJobProjection.StatusFunded, j =>
        {
            callbackInvoked = true;
            j.DeliverableCommitment = "test-deliverable";
            deliverableSet = j.DeliverableCommitment;
        });

        callbackInvoked.Should().BeTrue();
        var updatedJob = await _dbContext.AgenticJobs.FindAsync(job.Id);
        updatedJob!.DeliverableCommitment.Should().Be("test-deliverable");
    }

    [Fact]
    public async Task CreateJobAsync_WithDescriptionTooLong_ThrowsException()
    {
        var longDescription = new string('x', 257);
        await FluentActions.Invoking(() => _service.CreateJobAsync(
            "ethereum-sepolia",
            "0x1234567890123456789012345678901234567890",
            "0x2234567890123456789012345678901234567890",
            "0x3234567890123456789012345678901234567890",
            longDescription,
            100,
            DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()))
            .Should().ThrowAsync<ArgumentException>().WithMessage("Description commitment is too long.");
    }

    [Fact]
    public async Task CreateJobAsync_WithZeroProviderAddress_IsAllowed()
    {
        var job = await _service.CreateJobAsync(
            "ethereum-sepolia",
            "0x1234567890123456789012345678901234567890",
            "0x0000000000000000000000000000000000000000",
            "0x3234567890123456789012345678901234567890",
            "commit123",
            100,
            DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds());

        job.Should().NotBeNull();
        job.ProviderAddress.Should().Be("0x0000000000000000000000000000000000000000");
    }
}

