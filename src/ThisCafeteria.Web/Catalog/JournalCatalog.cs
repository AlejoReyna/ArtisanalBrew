namespace ThisCafeteria.Web.Catalog;

public static class JournalCatalog
{
    public static readonly IReadOnlyList<JournalArticle> FeaturedArticles =
    [
        new(
            "the-bloom",
            "Volume IV • Issue II",
            "The Bloom: A Ritual of Patience",
            "Exploring the scientific beauty and meditative pause that unfold when water first meets the grounds.",
            "Featured",
            "8 min read",
            "bloom",
            "A careful V60 pour-over coffee ritual in warm morning light",
            IsFeature: true),
        new(
            "what-altitude-leaves-in-the-cup",
            "Field Notes • Harvest",
            "What Altitude Leaves in the Cup",
            "A closer look at cool mountain nights, slow ripening cherries, and the brightness they carry home.",
            "Origin",
            "6 min read",
            "altitude",
            "Coffee cherries ripening on a green mountain farm"),
        new(
            "the-quiet-math-of-water",
            "Brew Bar • Technique",
            "The Quiet Math of Water",
            "How temperature, mineral balance, and a patient pour shape sweetness before the first sip.",
            "Brew Method",
            "5 min read",
            "water",
            "Steam rising from a kettle beside a ceramic coffee dripper"),
        new(
            "designing-a-slower-morning",
            "Counter Culture • Service",
            "Designing a Slower Morning",
            "On cafe rituals, familiar cups, and the small details that turn a daily stop into a pause.",
            "Space",
            "4 min read",
            "morning",
            "A calm cafe counter with cups arranged for morning service",
            IsDark: true)
    ];

    public static readonly IReadOnlyList<JournalArticle> ShortStories =
    [
        new(
            "shade-grown-lots",
            "Origin Focus",
            "Shade-Grown Lots from the Western Ridge",
            "What canopy, altitude, and hand sorting bring to a cup with soft citrus and toasted almond.",
            "Origin Focus",
            "3 min read",
            "origin",
            "Raw coffee beans gathered on a linen surface"),
        new(
            "quiet-geometry-v60",
            "Brewing Tips",
            "The Quiet Geometry of the V60",
            "A practical note on spiral pours, drawdown rhythm, and the small corrections worth noticing.",
            "Brewing Tips",
            "4 min read",
            "tools",
            "Pour-over brewing tools arranged beside a ceramic cup"),
        new(
            "reading-while-kettle-cools",
            "Slow Living",
            "Reading While the Kettle Cools",
            "On building a morning pause around the minutes that usually vanish before the first sip.",
            "Slow Living",
            "3 min read",
            "reading",
            "A calm table with coffee, a book, and afternoon light")
    ];

    public static readonly IReadOnlyList<JournalArticle> HomeArticles =
        FeaturedArticles.Take(3).ToArray();

    public static string HeroImageClass(string imageKey) => $"journal-hero__image journal-hero__image--{imageKey}";

    public static string StoryImageClass(string imageKey) => $"journal-story-card__image journal-story-card__image--{imageKey}";

    public static string HomeImageClass(string imageKey) => $"recruiter-journal-entry__visual journal-thumb journal-thumb--{imageKey}";

    public static string ArticleUrl(string slug) => $"journal/{slug}";

    public static bool TryGetArticle(string slug, out JournalArticle? article)
    {
        article = AllArticles.FirstOrDefault(entry =>
            string.Equals(entry.Slug, slug, StringComparison.OrdinalIgnoreCase));
        return article is not null;
    }

    public static IReadOnlyList<JournalArticle> AllArticles =>
        FeaturedArticles.Concat(ShortStories).ToArray();
}
