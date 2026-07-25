using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Org.BouncyCastle.Crypto.Parameters;
using System.Security.Cryptography;
using System.Text.Json;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Web.Services.Blockchain;

namespace ThisCafeteria.UnitTests;

// Mutates a process environment variable, so keep these tests serial.
[CollectionDefinition("SolanaFaucetEnvSerial", DisableParallelization = true)]
public sealed class SolanaFaucetEnvSerialCollection;

[Collection("SolanaFaucetEnvSerial")]
public sealed class SolanaFaucetServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly SolanaFaucetOptions _options = new() { ClaimAmount = 100m, CooldownSeconds = 86_400 };

    public SolanaFaucetServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task StatusReportsNotConfiguredWhenTheAdminSecretIsAbsent()
    {
        Environment.SetEnvironmentVariable(SolanaFaucetSecret.EnvironmentVariable, null);
        var (service, chain) = CreateService("D5iN8pAhbzXN9Duq75GsKo6VPHwWzBbtYc1s5KH2CJGA");

        var status = await service.GetStatusAsync(chain.Key, "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM");

        status.IsConfigured.Should().BeFalse();
        status.CanClaim.Should().BeFalse();
        status.ClaimAmount.Should().Be(100m);
        status.CooldownSeconds.Should().Be(86_400);
    }

    [Fact]
    public async Task StatusReportsClaimableWhenConfiguredAndNoRecentClaim()
    {
        var (secretJson, administrator) = GenerateAdminSecret();
        Environment.SetEnvironmentVariable(SolanaFaucetSecret.EnvironmentVariable, secretJson);
        try
        {
            var (service, chain) = CreateService(administrator);
            var status = await service.GetStatusAsync(chain.Key, "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM");

            status.IsConfigured.Should().BeTrue();
            status.CanClaim.Should().BeTrue();
            status.NextClaimAtUtc.Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(SolanaFaucetSecret.EnvironmentVariable, null);
        }
    }

    [Fact]
    public async Task StatusEnforcesTheCooldownAfterARecentClaimThenClearsWhenItElapses()
    {
        var (secretJson, administrator) = GenerateAdminSecret();
        Environment.SetEnvironmentVariable(SolanaFaucetSecret.EnvironmentVariable, secretJson);
        try
        {
            const string wallet = "9WzDXwBbmkg8ZTbNMqUxvQRAyrZzDsGYdLVL9zYtAWWM";
            var (service, chain) = CreateService(administrator);
            _context.SolanaFaucetClaims.Add(new SolanaFaucetClaim { ChainKey = chain.Key, WalletAddress = wallet, Amount = 100m, ClaimedAtUtc = _time.GetUtcNow().UtcDateTime });
            await _context.SaveChangesAsync();

            var blocked = await service.GetStatusAsync(chain.Key, wallet);
            blocked.CanClaim.Should().BeFalse();
            blocked.NextClaimAtUtc.Should().Be(_time.GetUtcNow().UtcDateTime.AddSeconds(86_400));

            _time.Advance(TimeSpan.FromSeconds(86_401));
            var cleared = await service.GetStatusAsync(chain.Key, wallet);
            cleared.CanClaim.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(SolanaFaucetSecret.EnvironmentVariable, null);
        }
    }

    private (SolanaFaucetService Service, ChainDefinition Chain) CreateService(string administrator)
    {
        var chain = new ChainDefinition
        {
            Key = "solana-devnet",
            Family = ChainFamily.Solana,
            Enabled = true,
            SolanaCluster = "devnet",
            PublicRpcUrl = "https://api.devnet.solana.com",
            ExplorerTransactionTemplate = "https://explorer.solana.com/tx/{0}?cluster=devnet",
            Capabilities = new ChainCapabilities { WalletLogin = true, LiquidStaking = true, Faucet = true },
            Deployment = new ChainDeployment
            {
                Admin = administrator,
                Cafe = "C7g7g34QzvmAiP4HMmdjWLgfV9Y8FSF4GcAXK97HLQEg",
                Token2022Program = "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb",
                CafeDecimals = 9
            }
        };
        var registry = new Mock<IChainRegistry>();
        registry.Setup(r => r.TryGet(chain.Key, out It.Ref<ChainDefinition>.IsAny))
            .Returns((string _, out ChainDefinition c) => { c = chain; return true; });

        var service = new SolanaFaucetService(
            registry.Object,
            Mock.Of<IHttpClientFactory>(),
            _context,
            Options.Create(_options),
            _time,
            Mock.Of<Microsoft.Extensions.Logging.ILogger<SolanaFaucetService>>());
        return (service, chain);
    }

    private static (string SecretJson, string Administrator) GenerateAdminSecret()
    {
        var seed = RandomNumberGenerator.GetBytes(32);
        var privateKey = new Ed25519PrivateKeyParameters(seed, 0);
        var publicKey = privateKey.GeneratePublicKey().GetEncoded();
        var secret64 = new byte[64];
        seed.CopyTo(secret64, 0);
        publicKey.CopyTo(secret64, 32);
        return (JsonSerializer.Serialize(Array.ConvertAll(secret64, b => (int)b)), SolanaTransactionBuilder.EncodeKey(publicKey));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
