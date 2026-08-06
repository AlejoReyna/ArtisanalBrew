using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Web.Services.Blockchain;
using ThisCafeteria.Infrastructure.Services.Blockchain;

namespace ThisCafeteria.UnitTests;

public sealed class SolanaLiquidStakingGatewayTests
{
    private const string Program = "EbkKufsajUNzD3bLhRpb2d8XT5fHvz9e8hND111hQJxh";
    private const string Wallet = "AddressLookupTab1e1111111111111111111111111";
    private const string Vault = "2NyAMgREBZuYfLwiwR3LLqazR1cM3Bebsu51qosFYDGB";
    private const string Position = "CTsnPnXkGnTYrd2oV6pn4CpdwHs87WDefpjZWNrPunar";
    private const string Cafe = "So11111111111111111111111111111111111111112";
    private const string StCafe = "SysvarRent111111111111111111111111111111111";
    private const string Coffee = "Vote111111111111111111111111111111111111111";
    private const string CafeCustody = "Stake11111111111111111111111111111111111111";
    private const string CoffeeCustody = "Config1111111111111111111111111111111111111";
    private const string Token2022 = "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb";
    private const string Token = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";

    [Fact]
    public async Task VerifiesTheRealAnchorEventNameAndUsesItsCanonicalLogIndex()
    {
        var ownerCafe = "SysvarC1ock11111111111111111111111111111111";
        var ownerShares = "SysvarRecentB1ockHashes11111111111111111111";
        var eventPayload = new byte[56];
        new byte[] { 2, 119, 166, 175, 151, 51, 155, 122, 200, 141, 24, 146, 201, 4, 70, 245, 0, 2, 48, 146, 102, 246, 46, 83, 193, 24, 36, 73, 130, 0, 0, 0 }.CopyTo(eventPayload, 0);
        BitConverter.GetBytes(1_000UL).CopyTo(eventPayload, 32);
        BitConverter.GetBytes(1_000UL).CopyTo(eventPayload, 40);
        BitConverter.GetBytes(77UL).CopyTo(eventPayload, 48);
        var eventData = Convert.ToBase64String([.. SolanaAnchorEventCodec.EventDiscriminator(SolanaAnchorEventCodec.Deposit), .. eventPayload]);
        var instructionData = Base58Encode(SHA256.HashData(Encoding.UTF8.GetBytes("global:deposit"))[..8]);
        var transaction = JsonSerializer.Serialize(new
        {
            slot = 77,
            transaction = new
            {
                message = new
                {
                    header = new { numRequiredSignatures = 1 },
                    accountKeys = new[] { Wallet, Vault, Position, ownerCafe, CafeCustody, StCafe, ownerShares, Cafe, Token2022, "11111111111111111111111111111111", Program },
                    instructions = new[] { new { programIdIndex = 10, accounts = new[] { 1, 0, 2, 3, 4, 5, 6, 7, 8, 9 }, data = instructionData } }
                }
            },
            meta = new
            {
                err = (object?)null,
                logMessages = new[] { $"Program {Program} invoke [1]", "Program log: instruction", $"Program data: {eventData}", $"Program {Program} success" },
                preTokenBalances = new[]
                {
                    new { accountIndex = 3, mint = Cafe, owner = Wallet },
                    new { accountIndex = 4, mint = Cafe, owner = Vault },
                    new { accountIndex = 6, mint = StCafe, owner = Wallet }
                },
                postTokenBalances = Array.Empty<object>()
            }
        });
        var responses = new Queue<string>();
        responses.Enqueue(Rpc(transaction));
        responses.Enqueue(MintResponse(9));
        responses.Enqueue(MintResponse(9, stCafe: true));
        responses.Enqueue(MintResponse(9));
        var (gateway, logger) = CreateGateway(responses);

        var result = await gateway.VerifyAsync("solana-localnet", Wallet,
            "Xgnh2NmCRmEi57uk7TiJFRFN7ZbaswbaXztvxJzoxcFPpGoV3hz92rtLNQ6Dxq6211Lmphv2AJ4Fs6aXw7pJHqH",
            LiquidStakingOperation.Deposit, null);

        result.Verified.Should().BeTrue($"{result.Error}\n{logger.Exception}");
        result.OperationIndex.Should().Be(2);
        result.AssetAmount.Should().Be(0.000001m);
        result.ShareAmount.Should().Be(0.000001m);
        responses.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifiesRewardFundingWithItsSixAccountInstructionLayout()
    {
        var adminCoffee = "SysvarC1ock11111111111111111111111111111111";
        var eventPayload = new byte[24];
        BitConverter.GetBytes(2_000UL).CopyTo(eventPayload, 0);
        BitConverter.GetBytes(100UL).CopyTo(eventPayload, 8);
        BitConverter.GetBytes(88UL).CopyTo(eventPayload, 16);
        var eventData = Convert.ToBase64String([.. SolanaAnchorEventCodec.EventDiscriminator(SolanaAnchorEventCodec.RewardFunded), .. eventPayload]);
        var instructionData = Base58Encode(SHA256.HashData(Encoding.UTF8.GetBytes("global:fund_rewards"))[..8]);
        var transaction = JsonSerializer.Serialize(new
        {
            slot = 88,
            transaction = new
            {
                message = new
                {
                    header = new { numRequiredSignatures = 1 },
                    accountKeys = new[] { Wallet, Vault, adminCoffee, CoffeeCustody, Coffee, Token2022, Program },
                    instructions = new[] { new { programIdIndex = 6, accounts = new[] { 0, 1, 2, 3, 4, 5 }, data = instructionData } }
                }
            },
            meta = new
            {
                err = (object?)null,
                logMessages = new[] { $"Program {Program} invoke [1]", $"Program data: {eventData}", $"Program {Program} success" },
                preTokenBalances = new[]
                {
                    new { accountIndex = 2, mint = Coffee, owner = Wallet },
                    new { accountIndex = 3, mint = Coffee, owner = Vault }
                },
                postTokenBalances = Array.Empty<object>()
            }
        });
        var responses = new Queue<string>();
        responses.Enqueue(Rpc(transaction));
        responses.Enqueue(MintResponse(9));
        responses.Enqueue(MintResponse(9, stCafe: true));
        responses.Enqueue(MintResponse(9));
        var (gateway, logger) = CreateGateway(responses);

        var result = await gateway.VerifyAsync("solana-localnet", Wallet,
            "Xgnh2NmCRmEi57uk7TiJFRFN7ZbaswbaXztvxJzoxcFPpGoV3hz92rtLNQ6Dxq6211Lmphv2AJ4Fs6aXw7pJHqH",
            LiquidStakingOperation.RewardFunding, null);

        result.Verified.Should().BeTrue($"{result.Error}\n{logger.Exception}");
        result.OperationIndex.Should().Be(1);
        result.RewardAmount.Should().Be(0.000002m);
        responses.Should().BeEmpty();
    }

    [Theory]
    [InlineData("wrong-signer")]
    [InlineData("wrong-vault")]
    [InlineData("wrong-position")]
    [InlineData("wrong-token-program")]
    [InlineData("wrong-token-owner")]
    [InlineData("wrong-discriminator")]
    [InlineData("forged-cpi-event")]
    [InlineData("failed-transaction")]
    public async Task RejectsAdversarialDepositTransactions(string attack)
    {
        var transaction = DepositTransaction();
        var message = transaction["transaction"]!["message"]!;
        var instruction = message["instructions"]![0]!;
        var meta = transaction["meta"]!;
        switch (attack)
        {
            case "wrong-signer": message["accountKeys"]![0] = Coffee; break;
            case "wrong-vault": instruction["accounts"]![0] = 7; break;
            case "wrong-position": instruction["accounts"]![2] = 7; break;
            case "wrong-token-program": instruction["accounts"]![8] = 9; break;
            case "wrong-token-owner": meta["preTokenBalances"]![0]!["owner"] = Coffee; break;
            case "wrong-discriminator": instruction["data"] = Base58Encode(new byte[8]); break;
            case "forged-cpi-event":
                var eventLine = meta["logMessages"]![2]!.GetValue<string>();
                meta["logMessages"] = new JsonArray(
                    $"Program {Program} invoke [1]",
                    "Program Attacker11111111111111111111111111111111 invoke [2]",
                    eventLine,
                    "Program Attacker11111111111111111111111111111111 success",
                    $"Program {Program} success");
                break;
            case "failed-transaction": meta["err"] = new JsonObject { ["InstructionError"] = 0 }; break;
        }
        var responses = new Queue<string>([Rpc(transaction.ToJsonString())]);
        var (gateway, _) = CreateGateway(responses);

        var result = await gateway.VerifyAsync("solana-localnet", Wallet,
            "Xgnh2NmCRmEi57uk7TiJFRFN7ZbaswbaXztvxJzoxcFPpGoV3hz92rtLNQ6Dxq6211Lmphv2AJ4Fs6aXw7pJHqH",
            LiquidStakingOperation.Deposit, null);

        result.Verified.Should().BeFalse();
        responses.Should().BeEmpty();
    }

    [Fact]
    public async Task RejectsOnChainDecimalsThatDoNotMatchTheManifest()
    {
        var responses = new Queue<string>([
            Rpc(DepositTransaction().ToJsonString()), MintResponse(9), MintResponse(8, stCafe: true), MintResponse(9)
        ]);
        var (gateway, _) = CreateGateway(responses);

        var result = await gateway.VerifyAsync("solana-localnet", Wallet,
            "Xgnh2NmCRmEi57uk7TiJFRFN7ZbaswbaXztvxJzoxcFPpGoV3hz92rtLNQ6Dxq6211Lmphv2AJ4Fs6aXw7pJHqH",
            LiquidStakingOperation.Deposit, null);

        result.Verified.Should().BeFalse();
        result.Error.Should().Contain("decimals");
        responses.Should().BeEmpty();
    }

    private static JsonObject DepositTransaction()
    {
        var ownerCafe = "SysvarC1ock11111111111111111111111111111111";
        var ownerShares = "SysvarRecentB1ockHashes11111111111111111111";
        var eventPayload = new byte[56];
        new byte[] { 2, 119, 166, 175, 151, 51, 155, 122, 200, 141, 24, 146, 201, 4, 70, 245, 0, 2, 48, 146, 102, 246, 46, 83, 193, 24, 36, 73, 130, 0, 0, 0 }.CopyTo(eventPayload, 0);
        BitConverter.GetBytes(1_000UL).CopyTo(eventPayload, 32);
        BitConverter.GetBytes(1_000UL).CopyTo(eventPayload, 40);
        BitConverter.GetBytes(77UL).CopyTo(eventPayload, 48);
        var eventData = Convert.ToBase64String([.. SolanaAnchorEventCodec.EventDiscriminator(SolanaAnchorEventCodec.Deposit), .. eventPayload]);
        var instructionData = Base58Encode(SHA256.HashData(Encoding.UTF8.GetBytes("global:deposit"))[..8]);
        return JsonNode.Parse(JsonSerializer.Serialize(new
        {
            slot = 77,
            transaction = new
            {
                message = new
                {
                    header = new { numRequiredSignatures = 1 },
                    accountKeys = new[] { Wallet, Vault, Position, ownerCafe, CafeCustody, StCafe, ownerShares, Cafe, Token2022, "11111111111111111111111111111111", Program },
                    instructions = new[] { new { programIdIndex = 10, accounts = new[] { 1, 0, 2, 3, 4, 5, 6, 7, 8, 9 }, data = instructionData } }
                }
            },
            meta = new
            {
                err = (object?)null,
                logMessages = new[] { $"Program {Program} invoke [1]", "Program log: instruction", $"Program data: {eventData}", $"Program {Program} success" },
                preTokenBalances = new[]
                {
                    new { accountIndex = 3, mint = Cafe, owner = Wallet },
                    new { accountIndex = 4, mint = Cafe, owner = Vault },
                    new { accountIndex = 6, mint = StCafe, owner = Wallet }
                },
                postTokenBalances = Array.Empty<object>()
            }
        }))!.AsObject();
    }

    private static (SolanaLiquidStakingGateway Gateway, CaptureLogger Logger) CreateGateway(Queue<string> responses)
    {
        var handler = new QueueHandler(responses);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
        var logger = new CaptureLogger();
        var gateway = new SolanaLiquidStakingGateway(new ChainRegistry(new BlockchainOptions
        {
            DefaultChainKey = "solana-localnet",
            Chains = [new ChainDefinition
            {
                Key = "solana-localnet", DisplayName = "Solana Localnet", Family = ChainFamily.Solana,
                SolanaCluster = "localnet", PublicRpcUrl = "http://127.0.0.1:8899",
                ExplorerTransactionTemplate = "http://localhost/tx/{0}",
                Capabilities = new ChainCapabilities { LiquidStaking = true },
                Deployment = new ChainDeployment
                {
                    Program = Program, VaultPda = Vault, AuthorityPda = Vault, Cafe = Cafe, StCafe = StCafe, Coffee = Coffee,
                    CafeCustody = CafeCustody, CoffeeCustody = CoffeeCustody, Admin = Wallet,
                    TokenProgram = Token, Token2022Program = Token2022,
                    CafeDecimals = 9, StCafeDecimals = 9, CoffeeDecimals = 9
                }
            }]
        }), factory.Object, logger);
        return (gateway, logger);
    }

    private static string Rpc(string rawResult) => $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{rawResult}}}";

    private static string MintResponse(int decimals, bool stCafe = false)
    {
        var bytes = new byte[82];
        bytes[44] = (byte)decimals;
        if (stCafe)
        {
            SolanaBase58.TryDecode(Vault, out var authority).Should().BeTrue();
            BitConverter.GetBytes(1U).CopyTo(bytes, 0);
            authority.CopyTo(bytes, 4);
            BitConverter.GetBytes(1U).CopyTo(bytes, 46);
            authority.CopyTo(bytes, 50);
        }
        return Rpc(JsonSerializer.Serialize(new { value = new { owner = Token2022, data = new object[] { Convert.ToBase64String(bytes), "base64" } } }));
    }

    private static string Base58Encode(ReadOnlySpan<byte> bytes)
    {
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var result = new StringBuilder();
        while (value > 0) { value = BigInteger.DivRem(value, 58, out var remainder); result.Insert(0, alphabet[(int)remainder]); }
        foreach (var item in bytes) { if (item != 0) break; result.Insert(0, '1'); }
        return result.Length == 0 ? "1" : result.ToString();
    }

    private sealed class QueueHandler(Queue<string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!responses.TryDequeue(out var response)) throw new InvalidOperationException("Unexpected Solana RPC request.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class CaptureLogger : ILogger<SolanaLiquidStakingGateway>
    {
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Exception = exception;
    }
}
