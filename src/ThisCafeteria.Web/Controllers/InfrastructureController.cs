using Microsoft.AspNetCore.Mvc;
using ThisCafeteria.Web.Services.Monitoring;

namespace ThisCafeteria.Web.Controllers;

/// <summary>
/// Same-origin summary for the navbar's "Infraestructura" panel. The browser never talks to
/// Prometheus or Grafana: it receives this aggregated view, which carries no monitoring URLs,
/// credentials or internal service names.
/// </summary>
[ApiController]
[Route("api/infrastructure")]
public sealed class InfrastructureController(IInfrastructureSummaryService summaries) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<InfrastructureSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        if (!summaries.Enabled)
        {
            return NotFound();
        }

        // A stale monitoring summary must never be replayed as current by a cache or proxy.
        Response.Headers.CacheControl = "no-store";
        return Ok(await summaries.GetAsync(cancellationToken));
    }
}
