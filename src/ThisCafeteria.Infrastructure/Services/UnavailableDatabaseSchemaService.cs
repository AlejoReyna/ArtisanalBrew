using System.Globalization;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Infrastructure.Services;

/// <summary>
/// Stand-in used when no database is configured, so the public transparency endpoint returns
/// an empty-but-valid document instead of failing to resolve.
/// </summary>
public sealed class UnavailableDatabaseSchemaService : IDatabaseSchemaService
{
    public Task<DatabaseSchemaDto> GetSchemaAsync(
        bool includeCounts = true,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new DatabaseSchemaDto(
            GeneratedAtUtc: DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Provider: "none",
            TableCount: 0,
            ColumnCount: 0,
            RelationshipCount: 0,
            TotalRows: 0,
            Notes: ["No database is configured for this environment."],
            Tables: []));
}
