namespace ThisCafeteria.Web.Catalog;

public sealed record MarketplaceProductSummary(string Name, string Section, string ImageClass)
{
    public string Slug => string.Join('-', Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

public sealed record MarketplaceProductDetail(
    string Slug,
    string Name,
    string Section,
    string ImageClass,
    string Eyebrow,
    decimal Price,
    string Description,
    IReadOnlyList<string> FlavorNotes,
    IReadOnlyList<MarketplaceSpecRow> PrimarySpecs,
    string RitualTitle,
    string RitualMethodLabel,
    string RitualMethod,
    string RitualRatioLabel,
    string RitualRatio,
    string RitualTemperatureLabel,
    string RitualTemperature,
    string BrewGuideHref,
    string BrewGuideLabel,
    IReadOnlyList<MarketplaceSpecRow> SensorySpecs,
    string? RegionImageSrc = null);
