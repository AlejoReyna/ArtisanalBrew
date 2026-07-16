using Microsoft.Extensions.Diagnostics.HealthChecks;
using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.Web.HealthChecks;

public sealed class MigrationReadinessHealthCheck(IMigrationReadiness readiness) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (readiness.IsReady)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Database initialization completed."));
        }

        return Task.FromResult(readiness.Failure is { } failure
            ? HealthCheckResult.Unhealthy("Database initialization failed.", failure)
            : HealthCheckResult.Unhealthy("Database initialization is still running."));
    }
}
