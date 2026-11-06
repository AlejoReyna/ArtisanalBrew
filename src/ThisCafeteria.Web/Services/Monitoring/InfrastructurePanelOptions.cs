namespace ThisCafeteria.Web.Services.Monitoring;

/// <summary>
/// Deployment facts and monitoring wiring behind the navbar's "Infraestructura" panel.
/// Bound from the <c>InfrastructurePanel</c> configuration section (environment variables
/// <c>InfrastructurePanel__Provider</c>, <c>InfrastructurePanel__Prometheus__BaseUrl</c>, ...).
/// </summary>
/// <remarks>
/// Nothing here is ever echoed to the browser verbatim: the panel only receives the
/// <see cref="InfrastructureSummary"/> derived from it, so the Prometheus address,
/// credentials and internal service names stay on the server.
/// </remarks>
public sealed class InfrastructurePanelOptions
{
    public const string SectionName = "InfrastructurePanel";

    /// <summary>Master switch. When false the endpoint answers 404 and the panel shows "Sin configurar".</summary>
    public bool Enabled { get; set; } = true;

    public string Project { get; set; } = "ArtisanalBrew";

    /// <summary><c>atlantic</c> (VPS / Docker Compose) or <c>azure</c> (AKS). Anything else is "Sin configurar".</summary>
    public string? Provider { get; set; }

    /// <summary>Free-form environment label, e.g. <c>production</c> or <c>demo</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>Commit SHA or image tag of the running release (AKS: the pipeline's image tag).</summary>
    public string? Release { get; set; }

    /// <summary>Serves clearly-labelled sample metrics. Only honoured when the host environment is Development.</summary>
    public bool SampleData { get; set; }

    public PrometheusPanelOptions Prometheus { get; set; } = new();

    public GrafanaPanelOptions Grafana { get; set; } = new();
}

public sealed class PrometheusPanelOptions
{
    /// <summary>Internal base URL, e.g. <c>http://prometheus:9090</c>. Empty means "Sin configurar".</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Hard ceiling for every query round-trip.</summary>
    public int TimeoutSeconds { get; set; } = 2;

    /// <summary>A reading whose newest sample is older than this is reported as "Datos desactualizados".</summary>
    public int StaleAfterSeconds { get; set; } = 120;

    /// <summary>Summaries are cached so opening the panel repeatedly never fans out to Prometheus.</summary>
    public int CacheSeconds { get; set; } = 15;

    /// <summary>Kubernetes namespace used by the AKS query set (kube-state-metrics).</summary>
    public string Namespace { get; set; } = "artisanalbrew-demo";
}

public sealed class GrafanaPanelOptions
{
    /// <summary>Authorised public dashboard URL (https). Never an API or admin address.</summary>
    public string? Url { get; set; }

    /// <summary>True when the dashboard is only reachable through a tunnel or kubectl port-forward.</summary>
    public bool Private { get; set; }
}
