using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Infrastructure.Services;
using Xunit;

namespace ThisCafeteria.UnitTests;

public class SmartAccountServiceTests
{
    private readonly SmartAccountService _service;

    public SmartAccountServiceTests()
    {
        _service = new SmartAccountService(NullLogger<SmartAccountService>.Instance);
    }

    [Fact]
    public async Task IsConfiguredAsync_ReturnsFalse()
    {
        var result = await _service.IsConfiguredAsync("ethereum-sepolia");
        result.Should().BeFalse("the service is scaffolding and fail-closed");
    }

    [Fact]
    public async Task GetOrDeployAccountAsync_ThrowsNotSupportedException()
    {
        await FluentActions.Invoking(() => _service.GetOrDeployAccountAsync("ethereum-sepolia", "0x123"))
            .Should().ThrowAsync<NotSupportedException>()
            .WithMessage("Smart account deployment is not configured for chain 'ethereum-sepolia'.");
    }

    [Fact]
    public async Task HasSufficientSponsorshipQuotaAsync_ReturnsFalse()
    {
        var result = await _service.HasSufficientSponsorshipQuotaAsync("ethereum-sepolia", "0x123", 10.0m);
        result.Should().BeFalse("sponsorship is not implemented yet");
    }

    [Fact]
    public async Task RecordSponsorshipUsageAsync_ThrowsNotSupportedException()
    {
        await FluentActions.Invoking(() => _service.RecordSponsorshipUsageAsync("ethereum-sepolia", "0x123", 10.0m))
            .Should().ThrowAsync<NotSupportedException>()
            .WithMessage("Sponsorship is not configured for chain 'ethereum-sepolia'.");
    }

    [Fact]
    public async Task RevokeSessionPermissionsAsync_ThrowsNotSupportedException()
    {
        await FluentActions.Invoking(() => _service.RevokeSessionPermissionsAsync("ethereum-sepolia", "0x123"))
            .Should().ThrowAsync<NotSupportedException>()
            .WithMessage("Smart account sessions are not configured for chain 'ethereum-sepolia'.");
    }
}
