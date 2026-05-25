namespace ThisCafeteria.Web.Catalog;

public static class MarketplaceCatalog
{
    public const string BeansSection = "Beans";
    public const string EquipmentSection = "Equipment";
    public const string CeramicsSection = "Ceramics";
    public const string AllSection = "All";

    public static readonly IReadOnlyList<MarketplaceProductSummary> Summaries =
    [
        new("Ethiopia Yirgacheffe", BeansSection, "image-frame--espresso"),
        new("Colombia Huila", BeansSection, "image-frame--latte"),
        new("Guatemala Antigua", BeansSection, "image-frame--coldbrew"),
        new("Kenya Nyeri AA", BeansSection, "image-frame--espresso"),
        new("Costa Rica Tarrazu", BeansSection, "image-frame--latte"),
        new("Brazil Cerrado", BeansSection, "image-frame--coldbrew"),
        new("Rwanda Huye Mountain", BeansSection, "image-frame--espresso"),
        new("Peru Cajamarca", BeansSection, "image-frame--latte"),
        new("Mexico Chiapas", BeansSection, "image-frame--coldbrew"),
        new("Panama Boquete", BeansSection, "image-frame--espresso"),
        new("Ceramic Pour-Over Dripper", EquipmentSection, "image-frame--equipment"),
        new("Gooseneck Kettle", EquipmentSection, "image-frame--equipment-alt"),
        new("Burr Hand Grinder", EquipmentSection, "image-frame--equipment"),
        new("Digital Coffee Scale", EquipmentSection, "image-frame--equipment-alt"),
        new("Cold Brew Tower", EquipmentSection, "image-frame--coldbrew"),
        new("French Press", EquipmentSection, "image-frame--equipment"),
        new("AeroPress Kit", EquipmentSection, "image-frame--equipment-alt"),
        new("Reusable Metal Filter", EquipmentSection, "image-frame--equipment"),
        new("Espresso Tamper", EquipmentSection, "image-frame--equipment-alt"),
        new("Milk Frothing Pitcher", EquipmentSection, "image-frame--latte"),
        new("Stoneware Latte Cup", CeramicsSection, "image-frame--ceramics"),
        new("Handmade Espresso Cup", CeramicsSection, "image-frame--ceramics-alt"),
        new("Stacking Mug Set", CeramicsSection, "image-frame--ceramics"),
        new("Minimal Travel Tumbler", CeramicsSection, "image-frame--ceramics-alt"),
        new("Linen Cafe Tote", CeramicsSection, "image-frame--ceramics"),
        new("Walnut Serving Tray", CeramicsSection, "image-frame--ceramics-alt"),
        new("Porcelain Cupping Bowl", CeramicsSection, "image-frame--ceramics"),
        new("Speckled Sugar Jar", CeramicsSection, "image-frame--ceramics-alt"),
        new("Cotton Barista Apron", CeramicsSection, "image-frame--ceramics"),
        new("Brass Coffee Scoop", CeramicsSection, "image-frame--ceramics-alt")
    ];

    private static readonly Lazy<IReadOnlyDictionary<string, MarketplaceProductDetail>> Details =
        new(() => Summaries.ToDictionary(summary => summary.Slug, BuildDetail));

    public static MarketplaceProductDetail? TryGetBySlug(string slug) =>
        Details.Value.TryGetValue(slug, out var detail) ? detail : null;

    public static string ToSlug(string name) =>
        string.Join('-', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static MarketplaceProductDetail BuildDetail(MarketplaceProductSummary summary) =>
        summary.Section switch
        {
            BeansSection => BuildBeanDetail(summary),
            EquipmentSection => BuildEquipmentDetail(summary),
            CeramicsSection => BuildCeramicsDetail(summary),
            _ => throw new InvalidOperationException($"Unknown section: {summary.Section}")
        };

    private static MarketplaceProductDetail BuildBeanDetail(MarketplaceProductSummary summary)
    {
        if (BeanProfiles.TryGetValue(summary.Name, out var profile))
        {
            return new MarketplaceProductDetail(
                summary.Slug,
                summary.Name,
                summary.Section,
                summary.ImageClass,
                profile.Eyebrow,
                profile.Price,
                profile.Description,
                profile.FlavorNotes,
                profile.PrimarySpecs,
                "Brewing Ritual",
                "Method",
                profile.Method,
                "Ratio",
                profile.Ratio,
                "Temperature",
                profile.Temperature,
                "/story",
                "Full Brew Guide",
                profile.SensorySpecs);
        }

        var (country, region) = SplitOrigin(summary.Name);
        return new MarketplaceProductDetail(
            summary.Slug,
            summary.Name,
            summary.Section,
            summary.ImageClass,
            $"SINGLE ORIGIN / {ContinentFor(country)}",
            PriceFor(summary.Name, 24m, 32m),
            $"A patient, washed lot from {region}, selected for clarity in the cup and a calm, tea-like finish.",
            DefaultBeanNotes(country),
            DefaultBeanOrigin(country, region),
            "Brewing Ritual",
            "Method",
            "Hario V60",
            "Ratio",
            "1:16",
            "Temperature",
            "92°C",
            "/story",
            "Full Brew Guide",
            [
                new("Roast Level", "Light–Medium"),
                new("Process", "Washed")
            ]);
    }

    private static MarketplaceProductDetail BuildEquipmentDetail(MarketplaceProductSummary summary)
    {
        var profile = EquipmentProfiles.GetValueOrDefault(summary.Name);
        return new MarketplaceProductDetail(
            summary.Slug,
            summary.Name,
            summary.Section,
            summary.ImageClass,
            profile?.Eyebrow ?? "BREWING EQUIPMENT",
            profile?.Price ?? PriceFor(summary.Name, 48m, 165m),
            profile?.Description ??
            "Designed for unhurried rituals—balanced weight, honest materials, and the quiet confidence of daily use.",
            profile?.FlavorNotes ?? ["Steady pour control", "Heat-retaining form", "Counter-worthy silhouette"],
            profile?.PrimarySpecs ?? DefaultEquipmentSpecs(summary.Name),
            "Usage Notes",
            "Recommended",
            profile?.Method ?? "Pour-over & immersion",
            "Capacity",
            profile?.Ratio ?? "1–4 cups",
            "Care",
            profile?.Temperature ?? "Hand wash, dry immediately",
            "/story",
            "Full Brew Guide",
            profile?.SensorySpecs ??
            [
                new("Finish", "Matte stoneware / brushed steel"),
                new("Origin", "Small-batch workshop")
            ]);
    }

    private static MarketplaceProductDetail BuildCeramicsDetail(MarketplaceProductSummary summary)
    {
        var profile = CeramicsProfiles.GetValueOrDefault(summary.Name);
        return new MarketplaceProductDetail(
            summary.Slug,
            summary.Name,
            summary.Section,
            summary.ImageClass,
            profile?.Eyebrow ?? "CERAMICS & GOODS",
            profile?.Price ?? PriceFor(summary.Name, 22m, 88m),
            profile?.Description ??
            "Hand-finished pieces made for slow mornings—warm in the palm, soft at the rim, and meant to outlast trends.",
            profile?.FlavorNotes ?? ["Speckled glaze", "Comfort-weighted rim", "Dishwasher-safe where noted"],
            profile?.PrimarySpecs ?? DefaultCeramicsSpecs(summary.Name),
            "Care Ritual",
            "Serving",
            profile?.Method ?? "Latte & filter coffee",
            "Set",
            profile?.Ratio ?? "Single or set of two",
            "Care",
            profile?.Temperature ?? "Warm water rinse; avoid thermal shock",
            "/story",
            "Care & Use Guide",
            profile?.SensorySpecs ??
            [
                new("Glaze", "Satin matte"),
                new("Process", "Wheel-thrown, kiln-fired")
            ]);
    }

    private static (string Country, string Region) SplitOrigin(string name)
    {
        var space = name.IndexOf(' ');
        return space < 0
            ? (name, name)
            : (name[..space], name[(space + 1)..]);
    }

    private static string ContinentFor(string country) => country switch
    {
        "Ethiopia" or "Kenya" or "Rwanda" => "AFRICA",
        "Brazil" => "SOUTH AMERICA",
        _ => "AMERICAS"
    };

    private static decimal PriceFor(string name, decimal min, decimal max)
    {
        var hash = Math.Abs(name.GetHashCode());
        var spread = (decimal)(hash % 9) / 10m;
        return Math.Round(min + ((max - min) * spread), 2);
    }

    private static IReadOnlyList<string> DefaultBeanNotes(string country) => country switch
    {
        "Colombia" => ["Caramelized Sugar", "Red Apple", "Milk Chocolate"],
        "Guatemala" => ["Dark Cocoa", "Orange Zest", "Honeyed Finish"],
        "Kenya" => ["Blackcurrant", "Tomato Leaf", "Bright Acidity"],
        "Costa Rica" => ["White Grape", "Almond Brittle", "Clean Finish"],
        "Brazil" => ["Roasted Hazelnut", "Milk Chocolate", "Low Acidity"],
        "Rwanda" => ["Hibiscus", "Red Berry", "Silky Body"],
        "Peru" => ["Brown Sugar", "Dried Fig", "Gentle Spice"],
        "Mexico" => ["Panela", "Mild Citrus", "Nutty Sweetness"],
        "Panama" => ["Jasmine", "White Peach", "Structured Sweetness"],
        _ => ["Stone Fruit", "Brown Sugar", "Tea-like Finish"]
    };

    private static IReadOnlyList<MarketplaceSpecRow> DefaultBeanOrigin(string country, string region) =>
    [
        new("Region", $"{region}, {country}"),
        new("Altitude", country is "Ethiopia" or "Kenya" ? "1,900 – 2,200 m" : "1,400 – 1,800 m"),
        new("Varietal", country is "Ethiopia" ? "Heirloom" : "Caturra / Bourbon"),
        new("Harvest", "Winter 2024")
    ];

    private static IReadOnlyList<MarketplaceSpecRow> DefaultEquipmentSpecs(string name) =>
    [
        new("Material", name.Contains("Scale", StringComparison.OrdinalIgnoreCase) ? "Stainless steel / ABS" : "Steel & heat-resistant composite"),
        new("Dimensions", "Counter-scale; fits standard dripper"),
        new("Weight", "420 – 680 g"),
        new("Warranty", "12-month studio guarantee")
    ];

    private static IReadOnlyList<MarketplaceSpecRow> DefaultCeramicsSpecs(string name) =>
    [
        new("Material", name.Contains("Linen", StringComparison.OrdinalIgnoreCase) ? "Stone-washed linen" : "Stoneware / porcelain"),
        new("Capacity", name.Contains("Set", StringComparison.OrdinalIgnoreCase) ? "Set of four" : "240 – 320 ml"),
        new("Finish", "Satin exterior, glazed interior"),
        new("Origin", "Pacific Northwest kiln")
    ];

    private sealed record BeanProfile(
        string Eyebrow,
        decimal Price,
        string Description,
        IReadOnlyList<string> FlavorNotes,
        IReadOnlyList<MarketplaceSpecRow> PrimarySpecs,
        string Method,
        string Ratio,
        string Temperature,
        IReadOnlyList<MarketplaceSpecRow> SensorySpecs);

    private static readonly Dictionary<string, BeanProfile> BeanProfiles = new(StringComparer.Ordinal)
    {
        ["Ethiopia Yirgacheffe"] = new(
            "SINGLE ORIGIN / AFRICA",
            28.00m,
            "Grown in the misted highlands of Gediyo, this lot is washed with restraint so jasmine and citrus can stay luminous—not loud.",
            ["Jasmine Bloom", "Bright Lemon Zest", "Bergamot Tea"],
            [
                new("Region", "Gediyo, Ethiopia"),
                new("Altitude", "1,900 – 2,200 m"),
                new("Varietal", "Heirloom"),
                new("Harvest", "Winter 2024")
            ],
            "Hario V60",
            "1:16",
            "92°C",
            [
                new("Roast Level", "Light–Medium"),
                new("Process", "Washed")
            ]),
        ["Colombia Huila"] = new(
            "SINGLE ORIGIN / AMERICAS",
            26.00m,
            "A balanced Huila selection with caramel sweetness and a red-apple acidity that stays composed through the finish.",
            ["Caramelized Sugar", "Red Apple", "Milk Chocolate"],
            [
                new("Region", "Huila, Colombia"),
                new("Altitude", "1,600 – 1,900 m"),
                new("Varietal", "Caturra"),
                new("Harvest", "Spring 2024")
            ],
            "Kalita Wave",
            "1:15",
            "93°C",
            [
                new("Roast Level", "Medium"),
                new("Process", "Washed")
            ]),
        ["Kenya Nyeri AA"] = new(
            "SINGLE ORIGIN / AFRICA",
            30.00m,
            "AA-grade Nyeri with currant intensity and a structured acidity that rewards a patient pour and a slightly cooler cup.",
            ["Blackcurrant", "Tomato Leaf", "Bright Acidity"],
            [
                new("Region", "Nyeri, Kenya"),
                new("Altitude", "1,700 – 2,000 m"),
                new("Varietal", "SL28 / SL34"),
                new("Harvest", "Main Crop 2024")
            ],
            "Chemex",
            "1:17",
            "94°C",
            [
                new("Roast Level", "Light"),
                new("Process", "Washed")
            ]),
        ["Panama Boquete"] = new(
            "SINGLE ORIGIN / AMERICAS",
            32.00m,
            "Boquete terroir expressed with floral lift and stone-fruit sweetness—roasted lightly to preserve clarity.",
            ["Jasmine", "White Peach", "Structured Sweetness"],
            [
                new("Region", "Boquete, Panama"),
                new("Altitude", "1,400 – 1,700 m"),
                new("Varietal", "Geisha"),
                new("Harvest", "Winter 2024")
            ],
            "Origami Dripper",
            "1:16",
            "91°C",
            [
                new("Roast Level", "Light"),
                new("Process", "Washed")
            ])
    };

    private sealed record SectionProfile(
        string? Eyebrow,
        decimal? Price,
        string? Description,
        IReadOnlyList<string>? FlavorNotes,
        IReadOnlyList<MarketplaceSpecRow>? PrimarySpecs,
        string? Method,
        string? Ratio,
        string? Temperature,
        IReadOnlyList<MarketplaceSpecRow>? SensorySpecs);

    private static readonly Dictionary<string, SectionProfile> EquipmentProfiles = new(StringComparer.Ordinal)
    {
        ["Gooseneck Kettle"] = new(
            "BREWING EQUIPMENT",
            118.00m,
            "A slender spout for meditative pours—steady flow, soft grip, and enough capacity for a morning ritual for two.",
            ["Precision pour", "Balanced handle", "Stovetop compatible"],
            [
                new("Material", "Brushed stainless steel"),
                new("Capacity", "900 ml"),
                new("Weight", "780 g"),
                new("Warranty", "24-month studio guarantee")
            ],
            "Pour-over",
            "600 – 900 ml",
            "Hand wash only",
            [
                new("Finish", "Brushed steel"),
                new("Origin", "Osaka atelier")
            ]),
        ["Cold Brew Tower"] = new(
            "BREWING EQUIPMENT",
            142.00m,
            "Glass and steel tower for overnight steeping—clarity in the cup, drama on the counter.",
            ["Slow extraction", "Reusable filter", "Entertaining silhouette"],
            [
                new("Material", "Borosilicate glass / steel"),
                new("Capacity", "6–8 servings"),
                new("Weight", "1.2 kg"),
                new("Warranty", "12-month studio guarantee")
            ],
            "Immersion tower",
            "1:8 concentrate",
            "Room temperature steep",
            [
                new("Finish", "Clear glass"),
                new("Origin", "Portland workshop")
            ])
    };

    private static readonly Dictionary<string, SectionProfile> CeramicsProfiles = new(StringComparer.Ordinal)
    {
        ["Stoneware Latte Cup"] = new(
            "CERAMICS & GOODS",
            34.00m,
            "A generous bowl-lip cup that cradles microfoam—stoneware body, satin exterior, and a rim thin enough to feel effortless.",
            ["Speckled glaze", "Comfort-weighted rim", "Microwave safe"],
            [
                new("Material", "Stoneware"),
                new("Capacity", "300 ml"),
                new("Finish", "Satin exterior, glazed interior"),
                new("Origin", "Pacific Northwest kiln")
            ],
            "Latte & flat white",
            "Single vessel",
            "Warm water rinse",
            [
                new("Glaze", "Oat speckle"),
                new("Process", "Wheel-thrown")
            ]),
        ["Handmade Espresso Cup"] = new(
            "CERAMICS & GOODS",
            28.00m,
            "A demitasse with a narrowed foot and flared lip—made for short pulls and unhurried sips.",
            ["Thick-walled base", "Glazed interior", "Demitasse scale"],
            [
                new("Material", "Porcelain"),
                new("Capacity", "80 ml"),
                new("Finish", "Gloss interior"),
                new("Origin", "Vancouver Island studio")
            ],
            "Espresso",
            "Single shot",
            "Hand wash preferred",
            [
                new("Glaze", "Ivory satin"),
                new("Process", "Hand-thrown")
            ])
    };
}
