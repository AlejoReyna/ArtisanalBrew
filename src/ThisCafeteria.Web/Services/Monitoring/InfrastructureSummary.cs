using System.Text.Json.Serialization;

namespace ThisCafeteria.Web.Services.Monitoring;

/// <summary>Reading state shown on the Prometheus card. "Operativo" is only ever <see cref="Operational"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MonitoringState
{
    /// <summary>No (valid) monitoring source configured.</summary>
    NotConfigured,

    /// <summary>A source is configured but did not answer in time or answered with an error.</summary>
    Offline,

    /// <summary>The source answered, but the newest sample is too old or a metric is missing.</summary>
    Stale,

    /// <summary>Every metric was read recently.</summary>
    Operational
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DashboardAccess
{
    NotConfigured,

    /// <summary>Reachable only via tunnel / port-forward: show "Acceso privado", no link.</summary>
    Private,

    /// <summary>Authorised https URL.</summary>
    Link
}

public sealed record DeploymentDetails(
    string Project,
    string? Provider,
    string? Platform,
    string? Environment,
    IReadOnlyList<string> Components,
    string? Release);

public sealed record MetricReading(string Id, string Label, string Value);

public sealed record PrometheusSummary(
    MonitoringState State,
    IReadOnlyList<MetricReading> Metrics,
    DateTimeOffset? LastReading,
    bool IsSample);

public sealed record GrafanaSummary(DashboardAccess Access, string? Url);

public sealed record InfrastructureSummary(
    DeploymentDetails Deployment,
    PrometheusSummary Prometheus,
    GrafanaSummary Grafana)
{
    /// <summary>What a switched-off panel yields: no deployment facts, no source, no dashboard.</summary>
    public static InfrastructureSummary Disabled { get; } = new(
        new DeploymentDetails("ArtisanalBrew", null, null, null, [], null),
        new PrometheusSummary(MonitoringState.NotConfigured, [], null, false),
        new GrafanaSummary(DashboardAccess.NotConfigured, null));
}
