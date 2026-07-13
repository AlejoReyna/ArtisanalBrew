using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ThisCafeteria.Infrastructure.Identity;

namespace ThisCafeteria.Infrastructure.Persistence;

public sealed class MigrationHostedService : IHostedService
{
    private readonly IServiceProvider _rootProvider;
    private readonly IConfiguration _configuration;
    private readonly IMigrationReadiness _readiness;
    private readonly ILogger<MigrationHostedService> _logger;
    private readonly bool _runMigrations;
    private Task? _executingTask;
    private readonly CancellationTokenSource _cts = new();

    public MigrationHostedService(
        IServiceProvider rootProvider,
        IConfiguration configuration,
        IMigrationReadiness readiness,
        ILogger<MigrationHostedService> logger,
        bool runMigrations)
    {
        _rootProvider = rootProvider;
        _configuration = configuration;
        _readiness = readiness;
        _logger = logger;
        _runMigrations = runMigrations;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _executingTask = Task.Run(() => RunAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!_runMigrations)
        {
            _readiness.MarkReady();
            return;
        }

        try
        {
            _logger.LogInformation("Starting database migrations and seeding in the background");

            await using var scope = _rootProvider.CreateAsyncScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.MigrateAsync(cancellationToken);

            await AdminIdentitySeeder.SeedAsync(scope.ServiceProvider, _configuration);

            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            await seeder.SeedAsync(cancellationToken);

            _readiness.MarkReady();
            _logger.LogInformation("Database migrations and seeding completed");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Database migrations and seeding were canceled");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Database migrations or seeding failed");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_executingTask is null)
        {
            return;
        }

        await _cts.CancelAsync();

        var timeout = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        var completed = await Task.WhenAny(_executingTask, timeout);
        if (completed != _executingTask)
        {
            _logger.LogWarning("Database migrations and seeding did not shut down gracefully within 30 seconds");
        }
    }
}
