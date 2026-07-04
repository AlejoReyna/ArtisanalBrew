using Serilog;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Infrastructure;
using ThisCafeteria.Infrastructure.Configuration;
using ThisCafeteria.Worker;

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

try
{
    LocalDotEnvLoader.LoadIfPresent();

    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddSerilog();
    builder.Services.AddInfrastructure(builder.Configuration);

    var blockchainNetworkSection = builder.Configuration.GetSection(BlockchainNetworkOptions.SectionName);
    if (!blockchainNetworkSection.Exists())
    {
        blockchainNetworkSection = builder.Configuration.GetSection(BlockchainNetworkOptions.LegacySectionName);
    }

    builder.Services.Configure<BlockchainNetworkOptions>(blockchainNetworkSection);
    builder.Services.AddHostedService<OrderProcessingWorker>();
    builder.Services.AddHostedService<StakingLedgerReconciliationWorker>();

    var host = builder.Build();
    host.Run();
}
finally
{
    await Log.CloseAndFlushAsync();
}
