using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Services;
using Xunit;

namespace ThisCafeteria.UnitTests;

public sealed class RundlerBundlerClientTests
{
    private const string ChainKey = "evm-local";
    private const string EntryPoint = "0x8a791620dd6260079bf849dc5567adc3f2fdc318";
    private const string Sender = "0x0000000000000000000000000000000000000001";
    private const string UserOpHash = "0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task SendUserOperation_UsesTrustedEntryPointAndV07SplitFields()
    {
        JsonDocument? request = null;
        var calls = 0;
        using var http = new HttpClient(new Handler(message =>
        {
            request = JsonDocument.Parse(message.Content!.ReadAsStringAsync().Result);
            calls++;
            object result = calls == 1 ? new[] { EntryPoint } : UserOpHash;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, result }) };
        })) { BaseAddress = new Uri("http://bundler.test") };
        var client = new RundlerBundlerClient(http, new Registry(new ChainDefinition
        {
            Key = ChainKey, Family = ChainFamily.Evm, BundlerRpcUrl = "http://bundler.test/rpc",
            Deployment = new ChainDeployment { EntryPoint = EntryPoint }
        }));

        var hash = await client.SendUserOperationAsync(ChainKey, Operation("0x11111111111111111111111111111111111111112222"));

        hash.Should().Be(UserOpHash);
        var rpc = request!.RootElement;
        rpc.GetProperty("method").GetString().Should().Be("eth_sendUserOperation");
        rpc.GetProperty("params")[1].GetString().Should().Be(EntryPoint);
        var op = rpc.GetProperty("params")[0];
        op.GetProperty("factory").GetString().Should().Be("0x1111111111111111111111111111111111111111");
        op.GetProperty("factoryData").GetString().Should().Be("0x2222");
        op.GetProperty("nonce").GetString().Should().Be("0x2a");
    }

    [Fact]
    public async Task Receipt_ParsesRealBundlerSpecShape_WithNestedTransactionHash()
    {
        // Real eth_getUserOperationReceipt responses (Rundler, and the ERC-4337 bundler spec in
        // general) nest the mined transaction's hash under `receipt` and use `userOpHash`, not
        // `userOperationHash` - this reproduces that exact shape rather than BundlerReceipt's own
        // flat field names, which a naive Deserialize<BundlerReceipt> would silently mismatch.
        const string txHash = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                result = new
                {
                    userOpHash = UserOpHash,
                    sender = Sender,
                    nonce = "0x2a",
                    success = true,
                    actualGasCost = "0x1",
                    actualGasUsed = "0x1",
                    receipt = new { transactionHash = txHash, status = "0x1" }
                }
            })
        }));
        var client = new RundlerBundlerClient(http, new Registry(new ChainDefinition
        {
            Key = ChainKey, Family = ChainFamily.Evm, BundlerRpcUrl = "http://bundler.test/rpc",
            Deployment = new ChainDeployment { EntryPoint = EntryPoint }
        }));

        var receipt = await client.GetUserOperationReceiptAsync(ChainKey, UserOpHash);

        receipt.Should().NotBeNull();
        receipt!.UserOperationHash.Should().Be(UserOpHash);
        receipt.TransactionHash.Should().Be(txHash);
        receipt.Sender.Should().Be(Sender);
        receipt.Nonce.Should().Be(42);
        receipt.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Receipt_ReturnsNullBeforeBundlerMinesOperation()
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, result = (object?)null })
        }));
        var client = new RundlerBundlerClient(http, new Registry(new ChainDefinition
        {
            Key = ChainKey, Family = ChainFamily.Evm, BundlerRpcUrl = "http://bundler.test/rpc",
            Deployment = new ChainDeployment { EntryPoint = EntryPoint }
        }));

        var receipt = await client.GetUserOperationReceiptAsync(ChainKey, UserOpHash);

        receipt.Should().BeNull();
    }

    [Fact]
    public async Task MissingBundlerConfiguration_FailsClosed()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("must not call network")));
        var client = new RundlerBundlerClient(http, new Registry(new ChainDefinition { Key = ChainKey, Family = ChainFamily.Evm }));

        await FluentActions.Invoking(() => client.SendUserOperationAsync(ChainKey, Operation("0x")))
            .Should().ThrowAsync<NotSupportedException>();
    }

    private static BundlerUserOperation Operation(string initCode) => new()
    {
        Sender = Sender, Nonce = 42, InitCode = initCode, CallData = "0x12345678",
        AccountGasLimits = "0x" + new string('0', 64), PreVerificationGas = 100,
        GasFees = "0x" + new string('0', 64), Signature = "0xdeadbeef"
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(callback(request));
    }

    private sealed class Registry(params ChainDefinition[] items) : IChainRegistry
    {
        public string DefaultChainKey => items[0].Key;
        public IReadOnlyList<ChainDefinition> All => items;
        public bool TryGet(string key, out ChainDefinition definition)
        {
            definition = items.FirstOrDefault(x => x.Key == key)!;
            return definition is not null;
        }
        public ChainDefinition GetRequired(string key) => TryGet(key, out var definition) ? definition : throw new KeyNotFoundException(key);
    }
}
