using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Worker;

namespace ThisCafeteria.UnitTests;

public sealed class SolanaReconciliationPaginationTests
{
    [Fact]
    public async Task ReadsEveryPageBeforeReturningACompleteCursorWindow()
    {
        var responses = new Queue<string>([
            Rpc(Signatures(1_000, 2_000)),
            Rpc(Signatures(2, 1_000))
        ]);
        var supervisor = CreateSupervisor(responses);

        var signatures = await supervisor.ReadSignaturesForTestAsync(Chain(), string.Empty, 900, CancellationToken.None);

        signatures.Should().HaveCount(1_002);
        responses.Should().BeEmpty();
    }

    [Fact]
    public async Task RefusesToReturnAPartialWindowAtThePaginationLimit()
    {
        var responses = new Queue<string>(Enumerable.Range(0, 10)
            .Select(page => Rpc(Signatures(1_000, 20_000 - page * 1_000))));
        var supervisor = CreateSupervisor(responses);

        var action = () => supervisor.ReadSignaturesForTestAsync(Chain(), string.Empty, 0, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*10,000 signatures*");
        responses.Should().BeEmpty();
    }

    [Fact]
    public async Task PropagatesRpcFailuresWithoutProducingACursorWindow()
    {
        var responses = new Queue<string>(["{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32000,\"message\":\"temporary failure\"}}"]);
        var supervisor = CreateSupervisor(responses);

        var action = () => supervisor.ReadSignaturesForTestAsync(Chain(), string.Empty, 0, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*RPC error*");
    }

    private static SolanaReconciliationSupervisor CreateSupervisor(Queue<string> responses)
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var registry = new Mock<IChainRegistry>();
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(factory => factory.CreateClient(It.IsAny<string>())).Returns(new HttpClient(new QueueHandler(responses)));
        return new SolanaReconciliationSupervisor(scopeFactory.Object, registry.Object, httpFactory.Object, NullLogger<SolanaReconciliationSupervisor>.Instance);
    }

    private static ChainDefinition Chain() => new()
    {
        Key = "solana-localnet",
        Family = ChainFamily.Solana,
        PublicRpcUrl = "http://127.0.0.1:8899",
        Deployment = new ChainDeployment { Program = "program" }
    };

    private static string Signatures(int count, long newestSlot) => JsonSerializer.Serialize(Enumerable.Range(0, count)
        .Select(index => new { signature = $"signature-{newestSlot - index}", slot = newestSlot - index }));

    private static string Rpc(string result) => $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{result}}}";

    private sealed class QueueHandler(Queue<string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!responses.TryDequeue(out var response)) throw new InvalidOperationException("Unexpected Solana RPC request.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }
}
