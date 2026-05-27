namespace ThisCafeteria.Infrastructure.Configuration;

public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    /// <summary>
    /// When true, inserts the built-in demo catalog products on startup if their slugs are missing.
    /// Keep false in production so deploys do not repopulate deleted seed products.
    /// </summary>
    public bool SeedProductsOnStartup { get; init; }
}
