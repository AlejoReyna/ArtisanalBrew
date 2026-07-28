using Microsoft.AspNetCore.Mvc;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Web.Controllers;

/// <summary>
/// Public, unauthenticated window onto the café's database. Serves the structure of the live
/// EF Core model plus per-table row counts — never row contents — so visitors can check the
/// entity diagram on /story against what is actually deployed.
/// </summary>
[ApiController]
[Route("api/transparency")]
public sealed class TransparencyController(IDatabaseSchemaService schemaService) : ControllerBase
{
    [HttpGet("schema")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<DatabaseSchemaDto>> GetSchema(
        [FromQuery] bool counts = true,
        CancellationToken cancellationToken = default)
    {
        var schema = await schemaService.GetSchemaAsync(counts, cancellationToken);
        return Ok(schema);
    }

    /// <summary>Headline totals only — the numbers the Story page prints above the diagram.</summary>
    [HttpGet("stats")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<object>> GetStats(CancellationToken cancellationToken = default)
    {
        var schema = await schemaService.GetSchemaAsync(includeCounts: true, cancellationToken);
        return Ok(new
        {
            schema.GeneratedAtUtc,
            schema.Provider,
            schema.TableCount,
            schema.ColumnCount,
            schema.RelationshipCount,
            schema.TotalRows,
            Groups = schema.Tables
                .GroupBy(table => table.Group)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new
                {
                    Group = group.Key,
                    Tables = group.Count(),
                    Rows = group.Sum(table => table.Rows ?? 0)
                })
        });
    }
}
