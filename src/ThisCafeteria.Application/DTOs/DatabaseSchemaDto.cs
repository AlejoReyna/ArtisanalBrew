namespace ThisCafeteria.Application.DTOs;

/// <summary>
/// A public, read-only projection of the live EF Core model. Structure only — no row contents
/// ever leave through this shape, so it is safe to serve unauthenticated.
/// </summary>
public sealed record DatabaseSchemaDto(
    string GeneratedAtUtc,
    string Provider,
    int TableCount,
    int ColumnCount,
    int RelationshipCount,
    long TotalRows,
    IReadOnlyList<string> Notes,
    IReadOnlyList<SchemaTableDto> Tables);

public sealed record SchemaTableDto(
    string Entity,
    string Table,
    string Group,
    long? Rows,
    IReadOnlyList<SchemaColumnDto> Columns,
    IReadOnlyList<SchemaRelationshipDto> References);

public sealed record SchemaColumnDto(
    string Name,
    string Type,
    bool Nullable,
    bool PrimaryKey,
    bool ForeignKey,
    int? MaxLength);

public sealed record SchemaRelationshipDto(
    string FromColumn,
    string ToTable,
    string ToColumn,
    bool Required);
