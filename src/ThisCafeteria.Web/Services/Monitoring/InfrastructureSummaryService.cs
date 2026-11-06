using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ThisCafeteria.Web.Services.Monitoring;

public interface IInfrastructureSummaryService
{
    bool Enabled { get; }

    Task<InfrastructureSummary> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the summary the navbar panel renders: static facts from the deployment that serves
/// this page, plus aggregated metrics read from Prometheus with fixed queries and a hard
/// timeout. A monitoring outage degrades the panel only — it never throws into the page.
/// </summary>
public sealed partial class InfrastructureSummaryService : IInfrastructureSummaryService
{
    private const string AtlanticProvider = "atlantic";
    private const string AzureProvider = "azure";

    private readonly IOptionsMonitor<InfrastructurePanelOptions> _options;
    private readonly IPrometheusQueryClient _prometheus;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _clock;
    private readonly ILogger<InfrastructureSummaryService> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private InfrastructureSummary? _cached;
    private DateTimeOffset _cachedUntil;
    private DateTimeOffset? _lastGoodReading;

    public InfrastructureSummaryService(
        IOptionsMonitor<InfrastructurePanelOptions> options,
        IPrometheusQueryClient prometheus,
        IHostEnvironment environment,
        TimeProvider clock,
        ILogger<InfrastructureSummaryService> logger)
    {
        _options = options;
        _prometheus = prometheus;
        _environment = environment;
        _clock = clock;
        _logger = logger;
    }

    public bool Enabled => _options.CurrentValue.Enabled;

    public async Task<InfrastructureSummary> GetAsync(CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var now = _clock.GetUtcNow();

        if (_cached is { } fresh && now < _cachedUntil)
        {
            return fresh;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            now = _clock.GetUtcNow();
            if (_cached is { } again && now < _cachedUntil)
            {
                return again;
            }

            var summary = new InfrastructureSummary(
                BuildDeployment(options),
                await ReadPrometheusAsync(options, cancellationToken),
                BuildGrafana(options));

            _cached = summary;
            _cachedUntil = now.AddSeconds(Math.Clamp(options.Prometheus.CacheSeconds, 1, 300));
            return summary;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------------------------------------------------------------- deployment facts

    private DeploymentDetails BuildDeployment(InfrastructurePanelOptions options)
    {
        var profile = ProfileFor(options.Provider);
        return new DeploymentDetails(
            string.IsNullOrWhiteSpace(options.Project) ? "ArtisanalBrew" : options.Project.Trim(),
            profile?.Provider,
            profile?.Platform,
            Clean(options.Environment, 40),
            profile?.Components ?? [],
            ResolveRelease(options));
    }

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength && SafeLabel().IsMatch(trimmed) ? trimmed : null;
    }

    private static string? ResolveRelease(InfrastructurePanelOptions options)
    {
        var configured = options.Release?.Trim();
        if (string.IsNullOrEmpty(configured))
        {
            // Builds that stamp the commit (SourceRevisionId) expose it as "1.2.3+<sha>".
            var informational = typeof(InfrastructureSummaryService).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var plus = informational?.IndexOf('+') ?? -1;
            configured = plus >= 0 ? informational![(plus + 1)..] : null;
        }

        if (string.IsNullOrEmpty(configured))
        {
            return null;
        }

        if (CommitSha().IsMatch(configured))
        {
            return configured[..Math.Min(12, configured.Length)].ToLowerInvariant();
        }

        // Image tags look like "<sha>-<buildId>"; keep them whole but never free text.
        return configured.Length <= 64 && ReleaseTag().IsMatch(configured) ? configured : null;
    }

    private sealed record Profile(string Provider, string Platform, string[] Components);

    private static Profile? ProfileFor(string? provider) => provider?.Trim().ToLowerInvariant() switch
    {
        AtlanticProvider or "atlantic.net" or "vps" => new Profile(
            "Atlantic.Net",
            "VPS / Docker Compose",
            ["Web (.NET)", "Worker (.NET)", "Agent Gateway (Node)", "PostgreSQL 16", "Caddy"]),
        AzureProvider or "aks" => new Profile(
            "Azure",
            "AKS",
            ["Web (.NET)", "Worker (.NET)", "Agent Gateway (Node)", "Azure Database for PostgreSQL"]),
        _ => null
    };

    // ---------------------------------------------------------------- grafana

    private GrafanaSummary BuildGrafana(InfrastructurePanelOptions options)
    {
        if (options.Grafana.Private)
        {
            return new GrafanaSummary(DashboardAccess.Private, null);
        }

        var raw = options.Grafana.Url?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return new GrafanaSummary(DashboardAccess.NotConfigured, null);
        }

        // Only an https page address is ever handed to the browser; credentials in the URL,
        // plain http (outside Development) and non-web schemes are dropped.
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && _environment.IsDevelopment())))
        {
            return new GrafanaSummary(DashboardAccess.Link, uri.AbsoluteUri);
        }

        _logger.LogWarning("InfrastructurePanel:Grafana:Url is not an authorised https address; the panel will show it as not configured.");
        return new GrafanaSummary(DashboardAccess.NotConfigured, null);
    }

    // ---------------------------------------------------------------- prometheus

    private async Task<PrometheusSummary> ReadPrometheusAsync(InfrastructurePanelOptions options, CancellationToken cancellationToken)
    {
        if (options.SampleData && _environment.IsDevelopment())
        {
            return SampleSummary(options);
        }

        var profile = ProfileFor(options.Provider);
        var queries = profile is null ? null : QueriesFor(profile, options);
        if (queries is null || !PrometheusConfigured(options))
        {
            return new PrometheusSummary(MonitoringState.NotConfigured, [], null, false);
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(options.Prometheus.TimeoutSeconds, 1, 10));
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(timeout);

        try
        {
            var freshness = _prometheus.QueryScalarAsync(queries.AgeExpression, budget.Token);
            var values = queries.Metrics.Select(m => m.Expressions.Select(e => _prometheus.QueryScalarAsync(e, budget.Token)).ToArray()).ToArray();
            await Task.WhenAll(values.SelectMany(v => v).Append(freshness));

            var readAt = _clock.GetUtcNow();
            var readings = new List<MetricReading>();
            var complete = true;
            for (var i = 0; i < queries.Metrics.Length; i++)
            {
                var numbers = values[i].Select(t => t.Result).ToArray();
                if (numbers.Any(n => n is null))
                {
                    complete = false;
                    continue;
                }

                readings.Add(new MetricReading(queries.Metrics[i].Id, queries.Metrics[i].Label, queries.Metrics[i].Format(numbers.Select(n => n!.Value).ToArray())));
            }

            var age = freshness.Result;
            var recent = age is { } seconds && seconds >= 0 && seconds <= Math.Clamp(options.Prometheus.StaleAfterSeconds, 15, 3600);
            _lastGoodReading = readAt;

            return new PrometheusSummary(
                complete && recent ? MonitoringState.Operational : MonitoringState.Stale,
                readings,
                readAt,
                false);
        }
        catch (Exception exception) when (exception is MonitoringUnavailableException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Log the category only: the exception text can carry the internal address.
            _logger.LogWarning("Prometheus is unreachable for the infrastructure panel ({Reason}).", exception.GetType().Name);
            return new PrometheusSummary(MonitoringState.Offline, [], _lastGoodReading, false);
        }
    }

    private bool PrometheusConfigured(InfrastructurePanelOptions options) =>
        Uri.TryCreate(options.Prometheus.BaseUrl?.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        string.IsNullOrEmpty(uri.UserInfo);

    private sealed record MetricQuery(string Id, string Label, string[] Expressions, Func<double[], string> Format);

    private sealed record QuerySet(MetricQuery[] Metrics, string AgeExpression);

    private static QuerySet? QueriesFor(Profile profile, InfrastructurePanelOptions options)
    {
        if (profile.Provider == "Atlantic.Net")
        {
            // Node Exporter + cAdvisor on the VPS.
            return new QuerySet(
            [
                new("cpu", "CPU", ["100 * (1 - avg(rate(node_cpu_seconds_total{mode=\"idle\"}[2m])))"], v => Percent(v[0])),
                new("memory", "Memoria", ["100 * (1 - sum(node_memory_MemAvailable_bytes) / sum(node_memory_MemTotal_bytes))"], v => Percent(v[0])),
                new("containers", "Contenedores", ["count(container_last_seen{name!=\"\"})"], v => Math.Round(v[0]).ToString("0", CultureInfo.InvariantCulture))
            ],
            "time() - max(timestamp(node_memory_MemTotal_bytes))");
        }

        // AKS monitoring is its own environment's configuration: these queries assume
        // kube-state-metrics is scraped by whichever Prometheus BaseUrl points at.
        if (!KubernetesNamespace().IsMatch(options.Prometheus.Namespace))
        {
            return null;
        }

        var ns = options.Prometheus.Namespace;
        return new QuerySet(
        [
            new("pods", "Pods listos",
                [$"sum(kube_pod_status_ready{{namespace=\"{ns}\",condition=\"true\"}})", $"count(kube_pod_info{{namespace=\"{ns}\"}})"],
                v => $"{Math.Round(v[0]):0} / {Math.Round(v[1]):0}")
        ],
        $"time() - max(timestamp(kube_pod_info{{namespace=\"{ns}\"}}))");
    }

    private static string Percent(double value) =>
        $"{Math.Clamp(value, 0, 100).ToString("0", CultureInfo.InvariantCulture)} %";

    private PrometheusSummary SampleSummary(InfrastructurePanelOptions options)
    {
        var atlantic = string.Equals(ProfileFor(options.Provider)?.Provider, "Atlantic.Net", StringComparison.Ordinal);
        MetricReading[] metrics = atlantic
            ? [new("cpu", "CPU", "23 %"), new("memory", "Memoria", "58 %"), new("containers", "Contenedores", "6")]
            : [new("pods", "Pods listos", "4 / 4")];
        return new PrometheusSummary(MonitoringState.Operational, metrics, _clock.GetUtcNow(), true);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 ._/+-]*$")]
    private static partial Regex SafeLabel();

    [GeneratedRegex("^[0-9a-fA-F]{7,40}$")]
    private static partial Regex CommitSha();

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_.+-]*$")]
    private static partial Regex ReleaseTag();

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex KubernetesNamespace();
}
