using Serilog;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure;
using ThisCafeteria.Infrastructure.Configuration;
using ThisCafeteria.Infrastructure.Services;
using ThisCafeteria.Worker;
using System.Text.Json;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console().MinimumLevel.Information()
    .CreateLogger();

try
{
    LocalDotEnvLoader.LoadIfPresent();

    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddSerilog();
    builder.Services.AddInfrastructure(builder.Configuration);
    var blockchainOptions = builder.Configuration.GetSection(BlockchainOptions.SectionName).Get<BlockchainOptions>() ?? BlockchainOptions.CreateDefaults();
    if (blockchainOptions.Chains.Count == 0) blockchainOptions = BlockchainOptions.CreateDefaults();
    blockchainOptions = BlockchainManifestLoader.LoadDeploymentManifests(
        blockchainOptions,
        builder.Configuration["Blockchain:LocalEvmManifest"] ?? Environment.GetEnvironmentVariable("ARTISANALBREW_EVM_MANIFEST"),
        builder.Configuration["Blockchain:SolanaDeploymentManifest"] ?? builder.Configuration["Blockchain:LocalSolanaManifest"] ?? Environment.GetEnvironmentVariable("ARTISANALBREW_SOLANA_MANIFEST"));
    builder.Services.AddSingleton(blockchainOptions);
    builder.Services.AddSingleton<IChainRegistry>(new ChainRegistry(blockchainOptions));

    var blockchainNetworkSection = builder.Configuration.GetSection(BlockchainNetworkOptions.SectionName);
    if (!blockchainNetworkSection.Exists())
    {
        blockchainNetworkSection = builder.Configuration.GetSection(BlockchainNetworkOptions.LegacySectionName);
    }

    builder.Services.Configure<BlockchainNetworkOptions>(blockchainNetworkSection);
    builder.Services.AddHostedService<OrderProcessingWorker>();
    builder.Services.AddHostedService<StakingLedgerReconciliationWorker>();
    builder.Services.AddSingleton<IAgenticCommerceReconciliationApplicator, AgenticCommerceReconciliationApplicator>();
builder.Services.AddSingleton<IEscrowEventProvider, EvmEscrowEventProvider>();
    builder.Services.AddHostedService<AgenticCommerceReconciliationWorker>();
    builder.Services.AddHostedService<ChainReconciliationSupervisor>();
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<SolanaReconciliationSupervisor>();
    builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<SolanaReconciliationSupervisor>());

    // Cross-chain solver. Absent configuration yields a disabled (fail-closed, idle) worker.
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton(builder.Configuration
        .GetSection(CrossChainSolverOptions.SectionName)
        .Get<CrossChainSolverOptions>() ?? new CrossChainSolverOptions());
    builder.Services.AddSingleton<ISolverPolicyService, SolverPolicyService>();
    builder.Services.AddSingleton<ICrossChainIntentProvider, CrossChainIntentProvider>();
    builder.Services.AddSingleton<ICrossChainSolverExecutor, EvmCrossChainSolverExecutor>();
    builder.Services.AddHostedService<CrossChainSolverWorker>();

    var host = builder.Build();
    if (args.Length > 0 && args[0] == "--solana-backfill")
    {
        string? chainKey = null; long? start = null; long? end = null; var dryRun = false; var advance = false; var allowLargeRange = false;
        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--chain" when index + 1 < args.Length: chainKey = args[++index]; break;
                case "--start-slot" when index + 1 < args.Length && long.TryParse(args[++index], out var parsedStart): start = parsedStart; break;
                case "--end-slot" when index + 1 < args.Length && long.TryParse(args[++index], out var parsedEnd): end = parsedEnd; break;
                case "--dry-run": dryRun = true; break;
                case "--advance-live-cursor": advance = true; break;
                case "--allow-large-range": allowLargeRange = true; break;
                default: throw new ArgumentException("Usage: --solana-backfill --chain <key> --start-slot <inclusive> --end-slot <inclusive> [--dry-run] [--allow-large-range] [--advance-live-cursor]");
            }
        }
        if (string.IsNullOrWhiteSpace(chainKey) || start is null || end is null) throw new ArgumentException("Chain, start slot, and end slot are required.");
        var report = await host.Services.GetRequiredService<SolanaReconciliationSupervisor>().BackfillAsync(chainKey, start.Value, end.Value, dryRun, allowLargeRange, advance, CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(report));
        return;
    }
    host.Run();
}
finally
{
    await Log.CloseAndFlushAsync();
}
