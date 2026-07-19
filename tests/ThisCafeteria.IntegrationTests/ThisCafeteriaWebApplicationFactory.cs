using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ThisCafeteria.IntegrationTests;

public sealed class ThisCafeteriaWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string externalConnectionString;
    private readonly string solanaManifestPath;

    public ThisCafeteriaWebApplicationFactory()
    {
        externalConnectionString =
            Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION") ??
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ??
            throw new InvalidOperationException("Integration tests require TEST_POSTGRES_CONNECTION from the Apple Container PostgreSQL fixture; Docker/Testcontainers fallback is disabled.");

        // Program loads the local .env before WebApplicationFactory applies its test
        // configuration providers. Install the external fixture connection before
        // Program builds services so AddInfrastructure cannot capture the local port.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", externalConnectionString);
        solanaManifestPath = Path.Combine(Path.GetTempPath(), $"artisanalbrew-integration-solana-{Guid.NewGuid():N}.json");
        File.WriteAllText(solanaManifestPath, """
        {
          "schemaVersion":"1",
          "chainKey":"solana-localnet",
          "rpcUrl":"http://127.0.0.1:8899",
          "cluster":"localnet",
          "programId":"EbkKufsajUNzD3bLhRpb2d8XT5fHvz9e8hND111hQJxh",
          "deploymentSlot":1,
          "statePda":"2NyAMgREBZuYfLwiwR3LLqazR1cM3Bebsu51qosFYDGB",
          "authorityPda":"2NyAMgREBZuYfLwiwR3LLqazR1cM3Bebsu51qosFYDGB",
          "cafeMint":"So11111111111111111111111111111111111111112",
          "stCafeMint":"SysvarRent111111111111111111111111111111111",
          "coffeeMint":"Vote111111111111111111111111111111111111111",
          "cafeCustody":"Stake11111111111111111111111111111111111111",
          "coffeeCustody":"Config1111111111111111111111111111111111111",
          "administrator":"AddressLookupTab1e1111111111111111111111111",
          "tokenProgram":"TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA",
          "token2022Program":"TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb",
          "cafeDecimals":9,
          "stCafeDecimals":9,
          "coffeeDecimals":9
        }
        """);
        Environment.SetEnvironmentVariable("ARTISANALBREW_SOLANA_MANIFEST", solanaManifestPath);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = externalConnectionString
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Environment.SetEnvironmentVariable("ARTISANALBREW_SOLANA_MANIFEST", null);
        if (File.Exists(solanaManifestPath)) File.Delete(solanaManifestPath);
    }
}
