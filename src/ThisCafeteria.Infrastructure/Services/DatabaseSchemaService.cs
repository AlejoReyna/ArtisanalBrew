using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.Infrastructure.Services;

/// <summary>
/// Projects the live EF Core model into a publicly shareable description of the database.
/// Deliberately structure-and-counts only: no row contents are read, so the output carries
/// nothing identifying and can be served without authentication.
/// </summary>
public sealed class DatabaseSchemaService(
    AppDbContext dbContext,
    IMemoryCache cache,
    ILogger<DatabaseSchemaService> logger) : IDatabaseSchemaService
{
    private const string CacheKey = "transparency:schema";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>ASP.NET Identity's own tables are boilerplate and are withheld on purpose.</summary>
    private const string IdentityNamespace = "Microsoft.AspNetCore.Identity";

    private static readonly IReadOnlyList<string> PublicNotes =
    [
        "Structure and row counts only — no row contents are exposed through this endpoint.",
        "ASP.NET Identity tables (AspNetUsers, AspNetRoles, and friends) are withheld.",
        "Row counts are read live from the running database and cached for 60 seconds."
    ];

    public async Task<DatabaseSchemaDto> GetSchemaAsync(
        bool includeCounts = true,
        CancellationToken cancellationToken = default)
    {
        var key = $"{CacheKey}:{includeCounts}";
        if (cache.TryGetValue(key, out DatabaseSchemaDto? cached) && cached is not null)
        {
            return cached;
        }

        var entityTypes = dbContext.Model
            .GetEntityTypes()
            .Where(IsPublishable)
            .OrderBy(entity => entity.ClrType.Name, StringComparer.Ordinal)
            .ToArray();

        var counts = includeCounts
            ? await GetRowCountsAsync(entityTypes, cancellationToken)
            : new Dictionary<string, long>(StringComparer.Ordinal);

        var tables = entityTypes.Select(entity => MapTable(entity, counts)).ToArray();

        var schema = new DatabaseSchemaDto(
            GeneratedAtUtc: DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Provider: dbContext.Database.ProviderName ?? "unknown",
            TableCount: tables.Length,
            ColumnCount: tables.Sum(table => table.Columns.Count),
            RelationshipCount: tables.Sum(table => table.References.Count),
            TotalRows: tables.Sum(table => table.Rows ?? 0),
            Notes: PublicNotes,
            Tables: tables);

        cache.Set(key, schema, CacheDuration);
        return schema;
    }

    private static bool IsPublishable(IEntityType entity)
    {
        if (entity.IsOwned())
        {
            return false;
        }

        // Skip Identity's own tables, and any shared/join type without a CLR class of its own.
        var ns = entity.ClrType.Namespace ?? string.Empty;
        if (ns.StartsWith(IdentityNamespace, StringComparison.Ordinal))
        {
            return false;
        }

        return entity.GetTableName() is not null;
    }

    private static SchemaTableDto MapTable(IEntityType entity, IReadOnlyDictionary<string, long> counts)
    {
        var tableName = entity.GetTableName()!;
        var storeObject = StoreObjectIdentifier.Table(tableName, entity.GetSchema());

        var primaryKey = entity.FindPrimaryKey()?.Properties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);

        var foreignKeyProperties = entity.GetForeignKeys()
            .SelectMany(fk => fk.Properties)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var columns = entity.GetProperties()
            .Select(property => new SchemaColumnDto(
                Name: property.GetColumnName(storeObject) ?? property.Name,
                Type: property.GetColumnType(storeObject) ?? property.ClrType.Name,
                Nullable: property.IsNullable,
                PrimaryKey: primaryKey.Contains(property.Name),
                ForeignKey: foreignKeyProperties.Contains(property.Name),
                MaxLength: property.GetMaxLength()))
            .ToArray();

        var references = entity.GetForeignKeys()
            .Where(fk => fk.PrincipalEntityType.GetTableName() is not null)
            .Select(fk => new SchemaRelationshipDto(
                FromColumn: ColumnNameOf(fk.Properties[0], storeObject),
                ToTable: fk.PrincipalEntityType.GetTableName()!,
                ToColumn: fk.PrincipalKey.Properties[0].Name,
                Required: fk.IsRequired))
            .ToArray();

        return new SchemaTableDto(
            Entity: entity.ClrType.Name,
            Table: tableName,
            Group: GroupFor(entity.ClrType.Name),
            Rows: counts.TryGetValue(tableName, out var rows) ? rows : null,
            Columns: columns,
            References: references);
    }

    private static string ColumnNameOf(IProperty property, StoreObjectIdentifier storeObject)
        => property.GetColumnName(storeObject) ?? property.Name;

    /// <summary>Buckets tables into the subsystems the Story page draws as swim lanes.</summary>
    private static string GroupFor(string entityName) => entityName switch
    {
        "Product" or "Cart" or "CartItem" or "Order" or "OrderItem"
            or "Coupon" or "CouponRedemption" or "Receipt" => "Commerce",

        "UserProfile" or "WalletIdentity" or "WalletAuthChallenge"
            or "WalletStatusEvent" or "SmartAccountRecord" or "ApplicationUser" => "Identity & wallets",

        "StakingLedgerEntry" or "RewardClaim" or "StakingReconciliationCheckpoint"
            or "SolanaFaucetClaim" => "Staking",

        "TransparencyRecord" or "CrossChainSolverCheckpoint" or "CrossChainSolverFill" => "Settlement",

        _ when entityName.StartsWith("Agent", StringComparison.Ordinal) => "Agentic commerce",
        _ when entityName.StartsWith("Sponsorship", StringComparison.Ordinal) => "Agentic commerce",

        _ => "Other"
    };

    /// <summary>
    /// Counts every table in a single round trip. Table names come from the EF model, never
    /// from user input, and are quoted before interpolation.
    /// </summary>
    private async Task<Dictionary<string, long>> GetRowCountsAsync(
        IReadOnlyList<IEntityType> entities,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var tables = entities
            .Select(entity => (Table: entity.GetTableName()!, Schema: entity.GetSchema() ?? "public"))
            .Distinct()
            .ToArray();

        if (tables.Length == 0)
        {
            return counts;
        }

        var sql = new StringBuilder();
        for (var i = 0; i < tables.Length; i++)
        {
            if (i > 0)
            {
                sql.Append(" UNION ALL ");
            }

            sql.Append(CultureInfo.InvariantCulture, $"SELECT {Literal(tables[i].Table)} AS table_name, COUNT(*) AS row_count FROM {Quote(tables[i].Schema)}.{Quote(tables[i].Table)}");
        }

        try
        {
            // Owned by the DbContext — opened/closed through Database, never disposed here.
            var connection = dbContext.Database.GetDbConnection();
            await dbContext.Database.OpenConnectionAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = sql.ToString();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                counts[reader.GetString(0)] = reader.GetInt64(1);
            }
        }
        catch (Exception ex)
        {
            // The schema shape is still worth serving when the database is unreachable.
            logger.LogWarning(ex, "Transparency schema: row counts unavailable, serving structure only.");
            counts.Clear();
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }

        return counts;
    }

    private static string Quote(string identifier)
        => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Literal(string value)
        => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
