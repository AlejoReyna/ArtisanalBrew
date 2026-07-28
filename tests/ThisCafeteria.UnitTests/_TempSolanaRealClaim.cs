using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Org.BouncyCastle.Crypto.Parameters;
using System.Security.Cryptography;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Web.Services.Blockchain;

namespace ThisCafeteria.UnitTests;

// TEMPORARY: proves the real SolanaFaucetService performs an actual devnet broadcast (not a
// simulation) using the real admin key from ARTISANALBREW_SOLANA_ADMIN_KEY. Deleted after running.
public sealed class _TempSolanaRealClaim
{
    [Fact]
    public async Task PerformARealDevnetClaim()
    {
        var adminKeyRaw = Environment.GetEnvironmentVariable("ARTISANALBREW_SOLANA_ADMIN_KEY");
        Assert.False(string.IsNullOrWhiteSpace(adminKeyRaw), "ARTISANALBREW_SOLANA_ADMIN_KEY must be set for this proof run.");

        var chain = new ChainDefinition
        {
            Key = "solana-devnet",
            Family = ChainFamily.Solana,
            Enabled = true,
            SolanaCluster = "devnet",
            PublicRpcUrl = "https://api.devnet.solana.com",
            ServerRpcUrl = "https://api.devnet.solana.com",
            ExplorerTransactionTemplate = "https://explorer.solana.com/tx/{0}?cluster=devnet",
            Capabilities = new ChainCapabilities { WalletLogin = true, LiquidStaking = true, Faucet = true },
            Deployment = new ChainDeployment
            {
                Admin = "D5iN8pAhbzXN9Duq75GsKo6VPHwWzBbtYc1s5KH2CJGA",
                Cafe = "C7g7g34QzvmAiP4HMmdjWLgfV9Y8FSF4GcAXK97HLQEg",
                Token2022Program = "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb",
                CafeDecimals = 9
            }
        };

        var registry = new Mock<IChainRegistry>();
        registry.Setup(r => r.TryGet(chain.Key, out It.Ref<ChainDefinition>.IsAny))
            .Returns((string _, out ChainDefinition c) => { c = chain; return true; });

        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddHttpClient();
        using var provider = services.BuildServiceProvider();
        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

        // Fresh, random recipient (a throwaway devnet test wallet -- holds nothing of value).
        var recipientSeed = RandomNumberGenerator.GetBytes(32);
        var recipientPublicKey = new Ed25519PrivateKeyParameters(recipientSeed, 0).GeneratePublicKey().GetEncoded();
        var recipient = SolanaTransactionBuilder.EncodeKey(recipientPublicKey);

        var service = new SolanaFaucetService(
            registry.Object,
            httpClientFactory,
            db,
            Options.Create(new SolanaFaucetOptions { ClaimAmount = 1m, CooldownSeconds = 86_400 }),
            new FakeTimeProvider(DateTimeOffset.UtcNow),
            Mock.Of<ILogger<SolanaFaucetService>>());

        var result = await service.ClaimAsync(chain.Key, recipient);

        Console.WriteLine("CLAIM_RESULT_BEGIN");
        Console.WriteLine($"Success={result.Success}");
        Console.WriteLine($"Signature={result.Signature}");
        Console.WriteLine($"ExplorerUrl={result.ExplorerUrl}");
        Console.WriteLine($"Amount={result.Amount}");
        Console.WriteLine($"Error={result.Error}");
        Console.WriteLine($"Recipient={recipient}");
        Console.WriteLine("CLAIM_RESULT_END");

        Assert.True(result.Success, result.Error);
    }
}
