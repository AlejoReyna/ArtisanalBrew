using Serilog;
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
    builder.Services.AddHostedService<OrderProcessingWorker>();

    var host = builder.Build();
    host.Run();
}
finally
{
    await Log.CloseAndFlushAsync();
}
