using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Web.HealthChecks;

namespace ThisCafeteria.UnitTests;

public sealed class MigrationReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ShouldBeUnhealthyWhileInitializationRuns()
    {
        var readiness = new MigrationReadiness();
        var healthCheck = new MigrationReadinessHealthCheck(readiness);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("still running");
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldBeHealthyAfterInitializationCompletes()
    {
        var readiness = new MigrationReadiness();
        readiness.MarkReady();
        var healthCheck = new MigrationReadinessHealthCheck(readiness);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_ShouldExposeInitializationFailure()
    {
        var failure = new InvalidOperationException("database unavailable");
        var readiness = new MigrationReadiness();
        readiness.MarkFailed(failure);
        var healthCheck = new MigrationReadinessHealthCheck(readiness);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeSameAs(failure);
    }
}
