using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace ThisCafeteria.IntegrationTests;

public sealed class ThisCafeteriaWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = postgres.GetConnectionString()
            });
        });
    }

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await postgres.DisposeAsync();
        await DisposeAsync();
    }
}
