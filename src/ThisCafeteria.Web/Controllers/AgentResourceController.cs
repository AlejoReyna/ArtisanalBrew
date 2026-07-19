using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ThisCafeteria.Web.Controllers;

/// <summary>
/// Narrow, server-to-server resource surface for the TypeScript x402 gateway.
/// Business state remains owned by ASP.NET; the gateway only supplies payment
/// and transport metadata around these deterministic responses.
/// </summary>
[ApiController]
[Route("internal/agent/resources")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public sealed class AgentResourceController(IConfiguration configuration, ILogger<AgentResourceController> logger) : ControllerBase
{
    [HttpPost("search-products")]
    public IActionResult SearchProducts([FromBody] SearchProductsRequest request) =>
        Authorized(request, () => Ok(new { kind = "product-search", query = request.Query.Trim(), results = Array.Empty<object>() }));

    [HttpPost("brew-plan")]
    public IActionResult BrewPlan([FromBody] BrewPlanRequest request) =>
        Authorized(request, () => Ok(new { kind = "brew-plan", productId = request.ProductId, quantity = request.Quantity, planVersion = "local-1" }));

    [HttpPost("provenance")]
    public IActionResult Provenance([FromBody] ProvenanceRequest request) =>
        Authorized(request, () => Ok(new { kind = "provenance-report", productId = request.ProductId, evidence = Array.Empty<object>(), evidenceStatus = "not-seeded" }));

    [HttpPost("wholesale-quote")]
    public IActionResult WholesaleQuote([FromBody] WholesaleQuoteRequest request) =>
        Authorized(request, () => Ok(new
        {
            kind = "wholesale-quote",
            productId = request.ProductId,
            quantity = request.Quantity,
            currency = "USDC",
            totalMinor = request.Quantity * 200000L,
            quoteVersion = "local-1",
            expiresInSeconds = 900
        }));

    private IActionResult Authorized(AgentRequest request, Func<IActionResult> response)
    {
        var configured = configuration["AgentGateway:ServiceSecret"] ?? Environment.GetEnvironmentVariable("AGENT_GATEWAY_SERVICE_SECRET");
        var supplied = Request.Headers["x-agent-gateway-secret"].ToString();
        if (string.IsNullOrWhiteSpace(configured) || !FixedTimeEquals(configured, supplied))
        {
            logger.LogWarning("Rejected agent gateway request for {Path}", Request.Path);
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.CorrelationId) || request.CorrelationId.Length > 96)
            return BadRequest(new { error = "correlation id required" });
        if (request.RequestHash is not null && request.RequestHash.Length != 64)
            return BadRequest(new { error = "request hash must be sha256 hex" });
        HttpContext.Response.Headers["x-correlation-id"] = request.CorrelationId;
        return response();
    }

    private static bool FixedTimeEquals(string expected, string supplied)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    public abstract record AgentRequest(string CorrelationId, string? RequestHash);
    public sealed record SearchProductsRequest(string Query, string CorrelationId, string? RequestHash) : AgentRequest(CorrelationId, RequestHash);
    public sealed record BrewPlanRequest(string ProductId, int Quantity, string CorrelationId, string? RequestHash) : AgentRequest(CorrelationId, RequestHash);
    public sealed record ProvenanceRequest(string ProductId, string CorrelationId, string? RequestHash) : AgentRequest(CorrelationId, RequestHash);
    public sealed record WholesaleQuoteRequest(string ProductId, int Quantity, string CorrelationId, string? RequestHash) : AgentRequest(CorrelationId, RequestHash);
}
