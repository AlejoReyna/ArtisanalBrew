using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Web.Services.Cart;
using ThisCafeteria.Web.Services.Monitoring;

namespace ThisCafeteria.IntegrationTests;

/// <summary>The panel's same-origin summary endpoint, including a dead metrics source.</summary>
public class InfrastructureControllerTests : IClassFixture<WebApplicationFactory<ThisCafeteria.Web.WebMarker>>
{
    private readonly WebApplicationFactory<ThisCafeteria.Web.WebMarker> _factory;

    public InfrastructureControllerTests(WebApplicationFactory<ThisCafeteria.Web.WebMarker> factory) => _factory = factory;

    private HttpClient Client(bool metricsDown, string? provider = "atlantic", bool enabled = true, StubPrometheus? stub = null) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "",
                ["ConnectionStrings:DefaultConnection"] = "",
                ["InfrastructurePanel:Enabled"] = enabled.ToString(),
                ["InfrastructurePanel:Provider"] = provider,
                ["InfrastructurePanel:Prometheus:BaseUrl"] = "http://prometheus.internal-monitoring:9090",
                ["InfrastructurePanel:Grafana:Private"] = "true"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPrometheusQueryClient>();
                services.AddSingleton<IPrometheusQueryClient>(stub ?? new StubPrometheus(metricsDown));

                // The harness runs without a database, so the data-backed services the shared
                // header (NavMenu) injects are replaced by empty ones: the page can render.
                var cart = new Mock<IShoppingCartService>();
                cart.Setup(c => c.GetItemCountAsync()).ReturnsAsync(0);
                services.RemoveAll<IShoppingCartService>();
                services.AddScoped(_ => cart.Object);
                services.RemoveAll<IProfileService>();
                services.AddScoped(_ => Mock.Of<IProfileService>());
                services.RemoveAll<IIdentityAccountService>();
                services.AddScoped(_ => Mock.Of<IIdentityAccountService>());
            });
        }).CreateClient();

    [Fact]
    public async Task SummaryIsServedNoStoreWithoutMonitoringAddresses()
    {
        var response = await Client(metricsDown: false).GetAsync("/api/infrastructure/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("\"platform\":\"VPS / Docker Compose\"").And.Contain("\"state\":\"Operational\"");
        text.Should().NotContain("internal-monitoring").And.NotContain("9090");
    }

    [Fact]
    public async Task ADeadMetricsSourceStillAnswersAndTheSiteKeepsWorking()
    {
        var client = Client(metricsDown: true);

        var summary = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/infrastructure/summary");
        summary.GetProperty("prometheus").GetProperty("state").GetString().Should().Be("Offline");
        summary.GetProperty("deployment").GetProperty("platform").GetString().Should().Be("VPS / Docker Compose");

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WhenDisabledTheEndpointIsNotFound()
    {
        var response = await Client(metricsDown: false, enabled: false).GetAsync("/api/infrastructure/summary");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WhenDisabledThePageHasNoInfrastructureButtonOrPanelAndNothingQueriesPrometheus()
    {
        var prometheus = new StubPrometheus(down: false);
        var client = Client(metricsDown: false, enabled: false, stub: prometheus);

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Artisanal Brew", "the page itself keeps rendering");
        html.Should().NotContain("infra-trigger").And.NotContain("id=\"infra-panel\"").And.NotContain("Infraestructura");
        (await client.GetAsync("/api/infrastructure/summary")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        prometheus.Calls.Should().Be(0, "a disabled panel must not start any Prometheus query");
    }

    [Fact]
    public async Task WhenEnabledThePageHasTheInfrastructureButtonAndPanel()
    {
        var html = await (await Client(metricsDown: false).GetAsync("/")).Content.ReadAsStringAsync();

        html.Should().Contain("id=\"infra-trigger\"").And.Contain("id=\"infra-panel\"");
    }

    [Fact]
    public async Task ADeadMetricsSourceDoesNotStopThePageFromRendering()
    {
        var client = Client(metricsDown: true);

        var home = await client.GetAsync("/");

        home.StatusCode.Should().Be(HttpStatusCode.OK);
        (await home.Content.ReadAsStringAsync()).Should().Contain("id=\"infra-trigger\"");
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    internal sealed class StubPrometheus(bool down) : IPrometheusQueryClient
    {
        private int _calls;

        public int Calls => _calls;

        public Task<double?> QueryScalarAsync(string expression, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return down ? throw new MonitoringUnavailableException("down") : Task.FromResult<double?>(expression.StartsWith("time()") ? 5 : 42);
        }
    }
}
