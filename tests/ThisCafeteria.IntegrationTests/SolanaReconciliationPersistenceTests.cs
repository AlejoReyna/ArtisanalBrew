using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Infrastructure.Services.Reconciliation;

namespace ThisCafeteria.IntegrationTests;

public sealed class SolanaReconciliationPersistenceTests(ThisCafeteriaWebApplicationFactory factory) : IClassFixture<ThisCafeteriaWebApplicationFactory>
{
    private const string ChainKey = "solana-reconciliation-test";
    private const string Program = "EbkKufsajUNzD3bLhRpb2d8XT5fHvz9e8hND111hQJxh";

    [Fact]
    public async Task PersistsRestartSafeCursorAndDoesNotAdvanceItWhenTransactionLoadingFails()
    {
        _ = factory.CreateClient();
        await CleanAsync();
        var chain = Chain();
        var first = Supervisor(chain, new Queue<string>([
            Rpc(JsonSerializer.Serialize(new[] { new { signature = "signature-a", slot = 88 } })),
            Rpc(Transaction(2_000, 88))
        ]));

        await first.ReconcileOnceAsync(chain, CancellationToken.None);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.StakingLedgerEntries.CountAsync(entry => entry.ChainKey == ChainKey)).Should().Be(1);
            var checkpoint = await db.StakingReconciliationCheckpoints.SingleAsync(item => item.ChainKey == ChainKey);
            checkpoint.LastScannedSlot.Should().Be(88);
            checkpoint.LastScannedSignature.Should().Be("signature-a");
        }

        // A newly constructed worker represents a process restart and must read the
        // durable cursor instead of replaying or creating a second projection.
        var restarted = Supervisor(chain, new Queue<string>([Rpc("[]")]));
        await restarted.ReconcileOnceAsync(chain, CancellationToken.None);

        var failing = Supervisor(chain, new Queue<string>([
            Rpc(JsonSerializer.Serialize(new[] { new { signature = "signature-b", slot = 89 } })),
            Rpc("null")
        ]));
        var action = () => failing.ReconcileOnceAsync(chain, CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not advanced*");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.StakingLedgerEntries.CountAsync(entry => entry.ChainKey == ChainKey)).Should().Be(1);
            var checkpoint = await db.StakingReconciliationCheckpoints.SingleAsync(item => item.ChainKey == ChainKey);
            checkpoint.LastScannedSlot.Should().Be(88);
            checkpoint.LastScannedSignature.Should().Be("signature-a");
        }
        await CleanAsync();
    }

    private SolanaReconciliationSupervisor Supervisor(ChainDefinition chain, Queue<string> responses)
    {
        var registry = new ChainRegistry(new BlockchainOptions { DefaultChainKey = ChainKey, Chains = [chain] });
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(item => item.CreateClient(It.IsAny<string>())).Returns(new HttpClient(new QueueHandler(responses)));
        return new SolanaReconciliationSupervisor(factory.Services.GetRequiredService<IServiceScopeFactory>(), registry, httpFactory.Object, NullLogger<SolanaReconciliationSupervisor>.Instance);
    }

    private async Task CleanAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.StakingLedgerEntries.Where(entry => entry.ChainKey == ChainKey).ExecuteDeleteAsync();
        await db.StakingReconciliationCheckpoints.Where(item => item.ChainKey == ChainKey).ExecuteDeleteAsync();
    }

    private static ChainDefinition Chain() => new()
    {
        Key = ChainKey,
        DisplayName = "Solana reconciliation test",
        Family = ChainFamily.Solana,
        Enabled = true,
        SolanaCluster = "localnet",
        PublicRpcUrl = "http://127.0.0.1:8899",
        ExplorerTransactionTemplate = "http://localhost/tx/{0}",
        Capabilities = new ChainCapabilities { LiquidStaking = true },
        Deployment = new ChainDeployment
        {
            Program = Program,
            VaultPda = "2NyAMgREBZuYfLwiwR3LLqazR1cM3Bebsu51qosFYDGB",
            AuthorityPda = "2NyAMgREBZuYfLwiwR3LLqazR1cM3Bebsu51qosFYDGB",
            Cafe = "So11111111111111111111111111111111111111112",
            StCafe = "SysvarRent111111111111111111111111111111111",
            Coffee = "Vote111111111111111111111111111111111111111",
            CafeCustody = "Stake11111111111111111111111111111111111111",
            CoffeeCustody = "Config1111111111111111111111111111111111111",
            Admin = "AddressLookupTab1e1111111111111111111111111",
            TokenProgram = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA",
            Token2022Program = "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb",
            CafeDecimals = 9,
            StCafeDecimals = 9,
            CoffeeDecimals = 9,
            StartBlockOrSlot = 88
        }
    };

    private static string Transaction(ulong amount, ulong slot)
    {
        var payload = new byte[24];
        BitConverter.GetBytes(amount).CopyTo(payload, 0);
        BitConverter.GetBytes(100UL).CopyTo(payload, 8);
        BitConverter.GetBytes(slot).CopyTo(payload, 16);
        var data = Convert.ToBase64String([.. SolanaAnchorEventCodec.EventDiscriminator(SolanaAnchorEventCodec.RewardFunded), .. payload]);
        return JsonSerializer.Serialize(new
        {
            meta = new { err = (object?)null, logMessages = new[] { $"Program {Program} invoke [1]", $"Program data: {data}", $"Program {Program} success" } }
        });
    }

    private static string Rpc(string result) => $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{result}}}";

    private sealed class QueueHandler(Queue<string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!responses.TryDequeue(out var response)) throw new InvalidOperationException("Unexpected Solana RPC request.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") });
        }
    }
}
