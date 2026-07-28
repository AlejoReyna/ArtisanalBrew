using ThisCafeteria.Application.DTOs;

namespace ThisCafeteria.Application.Services;

public interface IDatabaseSchemaService
{
    /// <summary>
    /// Describes the live database structure. Includes per-table row counts when
    /// <paramref name="includeCounts"/> is set and the database is reachable.
    /// </summary>
    Task<DatabaseSchemaDto> GetSchemaAsync(bool includeCounts = true, CancellationToken cancellationToken = default);
}
