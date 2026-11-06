using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using ThisCafeteria.Web.Services.Monitoring;

namespace ThisCafeteria.UnitTests;

/// <summary>
/// The "Infraestructura" navbar panel's server-side summary: what each deployment is called,
/// when a reading may be called "Operativo", and that a dead metrics source never throws.
/// </summary>
public sealed class InfrastructureSummaryServiceTests
{
    private const string InternalUrl = "http://prometheus.internal-monitoring:9090";

    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 11, 6, 14, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task WithoutProviderEverythingIsNotConfigured()
    {
        var summary = await Service(new InfrastructurePanelOptions(), new FakePrometheus()).GetAsync();

        summary.Deployment.Provider.Should().BeNull();
        summary.Deployment.Platform.Should().BeNull();
        summary.Deployment.Components.Should().BeEmpty();
        summary.Prometheus.State.Should().Be(MonitoringState.NotConfigured);
        summary.Grafana.Access.Should().Be(DashboardAccess.NotConfigured);
    }

    [Theory]
    [InlineData("atlantic", "Atlantic.Net", "VPS / Docker Compose")]
    [InlineData("Atlantic.Net", "Atlantic.Net", "VPS / Docker Compose")]
    [InlineData("azure", "Azure", "AKS")]
    [InlineData("AKS", "Azure", "AKS")]
    public async Task TheProviderDecidesThePlatformName(string configured, string provider, string platform)
    {
        var summary = await Service(new InfrastructurePanelOptions { Provider = configured }, new FakePrometheus()).GetAsync();

        summary.Deployment.Provider.Should().Be(provider);
        summary.Deployment.Platform.Should().Be(platform);
        summary.Deployment.Components.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AFreshCompleteReadingIsOperational()
    {
        var prometheus = new FakePrometheus { Cpu = 37.4, Memory = 61.6, Containers = 7, AgeSeconds = 12 };

        var summary = await Service(Atlantic(), prometheus).GetAsync();

        summary.Prometheus.State.Should().Be(MonitoringState.Operational);
        summary.Prometheus.Metrics.Select(m => (m.Id, m.Value)).Should().Equal(("cpu", "37 %"), ("memory", "62 %"), ("containers", "7"));
        summary.Prometheus.LastReading.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task AnOldSampleIsStaleNeverOperational()
    {
        var summary = await Service(Atlantic(), new FakePrometheus { AgeSeconds = 900 }).GetAsync();

        summary.Prometheus.State.Should().Be(MonitoringState.Stale);
    }

    [Fact]
    public async Task AMissingMetricIsStaleNeverOperational()
    {
        var summary = await Service(Atlantic(), new FakePrometheus { Containers = null }).GetAsync();

        summary.Prometheus.State.Should().Be(MonitoringState.Stale);
        summary.Prometheus.Metrics.Should().NotContain(m => m.Id == "containers");
    }

    [Fact]
    public async Task NoSeriesAtAllIsStale()
    {
        var summary = await Service(Atlantic(), new FakePrometheus { AgeSeconds = null, Cpu = null, Memory = null, Containers = null }).GetAsync();

        summary.Prometheus.State.Should().Be(MonitoringState.Stale);
    }

    [Fact]
    public async Task ADeadSourceIsOfflineAndDoesNotThrow()
    {
        var service = Service(Atlantic(), new FakePrometheus { Fail = true });

        var summary = await service.GetAsync();

        summary.Prometheus.State.Should().Be(MonitoringState.Offline);
        summary.Prometheus.Metrics.Should().BeEmpty();
        summary.Deployment.Platform.Should().Be("VPS / Docker Compose");
    }

    [Fact]
    public async Task ASourceThatNeverAnswersIsOfflineWithinTheTimeLimit()
    {
        var options = Atlantic();
        options.Prometheus.TimeoutSeconds = 1;
        var service = Service(options, new FakePrometheus { Hang = true }, TimeProvider.System);

        var started = DateTime.UtcNow;
        var summary = await service.GetAsync();

        summary.Prometheus.State.Should().Be(MonitoringState.Offline);
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AnOutageKeepsTheLastGoodReadingTimeButNoValues()
    {
        var prometheus = new FakePrometheus();
        var clock = new FakeTimeProvider(Clock.GetUtcNow());
        var service = Service(Atlantic(), prometheus, clock);
        var first = await service.GetAsync();

        clock.Advance(TimeSpan.FromMinutes(5));
        prometheus.Fail = true;
        var second = await service.GetAsync();

        second.Prometheus.State.Should().Be(MonitoringState.Offline);
        second.Prometheus.Metrics.Should().BeEmpty();
        second.Prometheus.LastReading.Should().Be(first.Prometheus.LastReading);
    }

    [Fact]
    public async Task ReadingsAreCachedSoReopeningThePanelDoesNotQueryAgain()
    {
        var prometheus = new FakePrometheus();
        var service = Service(Atlantic(), prometheus);

        await service.GetAsync();
        var queriesAfterFirst = prometheus.Calls;
        await service.GetAsync();

        prometheus.Calls.Should().Be(queriesAfterFirst);
    }

    [Fact]
    public async Task TheSerializedSummaryNeverContainsMonitoringAddresses()
    {
        var options = Atlantic();
        options.Grafana.Private = true;
        options.Grafana.Url = "https://grafana.internal-monitoring/d/secret";

        var summary = await Service(options, new FakePrometheus()).GetAsync();
        var json = JsonSerializer.Serialize(summary);

        json.Should().NotContain("internal-monitoring").And.NotContain("9090").And.NotContain("secret");
        summary.Grafana.Should().Be(new GrafanaSummary(DashboardAccess.Private, null));
    }

    [Fact]
    public async Task AnAuthorisedHttpsDashboardIsALink()
    {
        var options = Atlantic();
        options.Grafana.Url = "https://grafana.example.com/d/abc";

        var summary = await Service(options, new FakePrometheus()).GetAsync();

        summary.Grafana.Access.Should().Be(DashboardAccess.Link);
        summary.Grafana.Url.Should().Be("https://grafana.example.com/d/abc");
    }

    [Theory]
    [InlineData("http://grafana.example.com/d/abc")]
    [InlineData("https://admin:pw@grafana.example.com/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    public async Task UnsafeDashboardAddressesAreDropped(string url)
    {
        var options = Atlantic();
        options.Grafana.Url = url;

        var summary = await Service(options, new FakePrometheus()).GetAsync();

        summary.Grafana.Access.Should().Be(DashboardAccess.NotConfigured);
        summary.Grafana.Url.Should().BeNull();
    }

    [Fact]
    public async Task SampleDataIsHonouredOnlyInDevelopment()
    {
        var options = Atlantic();
        options.SampleData = true;

        var development = await Service(options, new FakePrometheus { Fail = true }, environment: Environments.Development).GetAsync();
        var production = await Service(options, new FakePrometheus { Fail = true }, environment: Environments.Production).GetAsync();

        development.Prometheus.IsSample.Should().BeTrue();
        production.Prometheus.IsSample.Should().BeFalse();
        production.Prometheus.State.Should().Be(MonitoringState.Offline);
    }

    [Theory]
    [InlineData("3F2A9C1D7E5B4A6089C0D1E2F3A4B5C6D7E8F901", "3f2a9c1d7e5b")]
    [InlineData("a1b2c3d-1234", "a1b2c3d-1234")]
    [InlineData("<script>alert(1)</script>", null)]
    public async Task TheReleaseIsAShortCommitOrASafeTag(string configured, string? expected)
    {
        var options = new InfrastructurePanelOptions { Provider = "atlantic", Release = configured };

        var summary = await Service(options, new FakePrometheus()).GetAsync();

        summary.Deployment.Release.Should().Be(expected);
    }

    [Fact]
    public void PrometheusResponsesAreParsedStrictly()
    {
        PrometheusQueryClient.ParseScalar(Json("""{"status":"success","data":{"resultType":"vector","result":[{"metric":{},"value":[1.0,"42.5"]}]}}""")).Should().Be(42.5);
        PrometheusQueryClient.ParseScalar(Json("""{"status":"success","data":{"resultType":"vector","result":[]}}""")).Should().BeNull();
        PrometheusQueryClient.ParseScalar(Json("""{"status":"success","data":{"result":[{"value":[1,"NaN"]}]}}""")).Should().BeNull();

        var error = () => PrometheusQueryClient.ParseScalar(Json("""{"status":"error","errorType":"bad_data","error":"parse error"}"""));
        error.Should().Throw<MonitoringUnavailableException>();
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static InfrastructurePanelOptions Atlantic() => new()
    {
        Provider = "atlantic",
        Prometheus = { BaseUrl = InternalUrl }
    };

    private static InfrastructureSummaryService Service(
        InfrastructurePanelOptions options,
        IPrometheusQueryClient prometheus,
        TimeProvider? clock = null,
        string environment = "Production")
    {
        var monitor = new Mock<IOptionsMonitor<InfrastructurePanelOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(options);
        var host = new Mock<IHostEnvironment>();
        host.SetupGet(h => h.EnvironmentName).Returns(environment);
        return new InfrastructureSummaryService(monitor.Object, prometheus, host.Object, clock ?? Clock, NullLogger<InfrastructureSummaryService>.Instance);
    }

    private sealed class FakePrometheus : IPrometheusQueryClient
    {
        public double? Cpu { get; set; } = 20;
        public double? Memory { get; set; } = 50;
        public double? Containers { get; set; } = 6;
        public double? AgeSeconds { get; set; } = 10;
        public bool Fail { get; set; }
        public bool Hang { get; set; }
        public int Calls { get; private set; }

        public async Task<double?> QueryScalarAsync(string expression, CancellationToken cancellationToken)
        {
            Calls++;
            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (Fail)
            {
                throw new MonitoringUnavailableException($"down: {InternalUrl}");
            }

            if (expression.StartsWith("time()", StringComparison.Ordinal)) return AgeSeconds;
            if (expression.Contains("node_cpu_seconds_total", StringComparison.Ordinal)) return Cpu;
            if (expression.Contains("node_memory", StringComparison.Ordinal)) return Memory;
            if (expression.Contains("container_last_seen", StringComparison.Ordinal)) return Containers;
            throw new InvalidOperationException($"Unexpected query: {expression}");
        }
    }
}
