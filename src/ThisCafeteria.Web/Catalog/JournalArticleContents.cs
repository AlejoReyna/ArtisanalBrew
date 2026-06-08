namespace ThisCafeteria.Web.Catalog;

public static class JournalArticleContents
{
    public const string AuthorName = "Alexis Reyna";

    private static readonly IReadOnlyDictionary<string, JournalArticleBody> Bodies =
        new Dictionary<string, JournalArticleBody>(StringComparer.OrdinalIgnoreCase)
        {
            ["the-bloom"] = new(
                AuthorName,
                "June 2026",
                "There is a moment in every pour-over when the cup seems to hold its breath. Water meets dry grounds, and for a few seconds nothing appears to happen at all. Then the surface rises — a quiet dome of foam that looks, from above, like a small planet forming in real time.",
                [
                    new(
                        "The Science",
                        "Carbon dioxide, waiting",
                        "Freshly roasted coffee holds dissolved CO₂ trapped inside its cellular structure. When hot water arrives, that gas rushes outward, carrying aromatic compounds with it. The bloom is not decoration. It is the coffee exhaling after weeks of rest, and the quality of that exhale tells you more than the label ever will.",
                        "A generous bloom is the coffee's way of saying it was roasted recently and stored with care."),
                    new(
                        null,
                        "The pause that teaches",
                        "Most mornings, we pour through the bloom too quickly — not because we are impatient with the coffee, but because we are impatient with ourselves. The ritual asks for thirty to forty-five seconds of stillness while the dome settles and darkens at the edges. In that interval, the bed of grounds becomes uniformly wet, channels are prevented, and extraction begins on equal terms.",
                        null),
                    new(
                        "Field Note",
                        null,
                        "At the brew bar, we mark the bloom timer with a small brass disc turned face-down on the counter. When a guest asks what it is for, we simply say: \"It reminds us not to rush the part that cannot be rushed.\"",
                        IsAside: true),
                    new(
                        "Practice",
                        "What to watch for",
                        "A healthy bloom should swell evenly across the surface, not bubble aggressively in one corner. Pale, thin foam often signals older coffee or uneven grind distribution. Deep amber crema with a gentle hiss suggests freshness and a roast profile that still has sweetness to give. None of this requires a refractometer — only attention.",
                        "Watch the edges darken before you pour again. That color shift is the bloom telling you it is ready."),
                    new(
                        null,
                        "A ritual, not a rule",
                        "We do not treat the bloom as a test you pass or fail. Some coffees — particularly darker roasts with lower gas content — bloom modestly and still brew beautifully. The point is not perfection. The point is the pause: the small surrender of control that turns a morning habit into something you remember having done.",
                        null)
                ],
                "Next issue: a field report from the western ridge harvest, and notes on reading the crackle of a cooling tray."),
            ["what-altitude-leaves-in-the-cup"] = new(
                AuthorName,
                "May 2026",
                "Above 1,600 meters, coffee cherries ripen slowly. Cool nights interrupt the day's warmth, stretching the maturation window by weeks. What altitude takes in speed, it returns in complexity — a brightness that survives the roast, a sweetness that does not flatten under milk.",
                [
                    new(
                        "Origin",
                        "Cool air, slow sugar",
                        "At elevation, temperature swings are dramatic. Daytime sun builds sugars inside the cherry; nighttime cold slows the plant's metabolism and preserves acidity that would otherwise convert to simpler, flatter compounds. The result is a cup with lift — citrus, stone fruit, florals — rather than the chocolate-forward profile common at lower farms.",
                        "Altitude is not a quality guarantee. It is a condition that rewards patience."),
                    new(
                        null,
                        "What the mountain keeps",
                        "Farmers on the western ridge describe their lots as \"late and bright.\" Cherries are picked by hand over multiple passes because ripeness arrives in waves, not all at once. That selectivity costs time and labor, but it means every sack arriving at our roastery carries fruit picked at the same stage — not a mix of underripe and overripe that muddies the cup.",
                        null),
                    new(
                        "Tasting",
                        null,
                        "In a side-by-side cupping, the same variety grown at 900m and 1,800m tells two different stories. The lower lot is round, nutty, forgiving. The higher lot is nervy — red currant, jasmine, a finish that lingers like a sentence you want to read twice. Neither is better. But altitude leaves a fingerprint you can taste.",
                        IsAside: true),
                    new(
                        "Harvest",
                        "Hand sorting at dusk",
                        "Sorting happens in the last light, when the temperature drops and workers can see color clearly against the sorting table. Defects — insect damage, unripe green, overripe ferment — are removed one cherry at a time. This is the work altitude demands: slower harvests, smaller yields, and a cup that carries the mountain's patience home.",
                        "The brightness in the cup is the brightness of a cherry that was given time to become itself."),
                    new(
                        null,
                        "Bringing it home",
                        "We roast high-altitude lots lighter than our house blend, not to show off acidity but to preserve the work that went into growing it. A darker roast would caramelize what the mountain spent months building. In the cup, you should taste cool air, slow mornings, and the particular sweetness of something that was never hurried.",
                        null)
                ],
                "Coming soon: shade-grown lots from the same ridge, and how canopy cover changes the drying curve."),
            ["the-quiet-math-of-water"] = new(
                AuthorName,
                "April 2026",
                "Water makes up more than ninety-eight percent of a brewed cup, which means most of what you taste is not the bean at all — it is what dissolved into the water on its way through the grounds. Temperature, mineral content, and pour rhythm are the quiet variables that separate a flat cup from a sweet one.",
                [
                    new(
                        "Temperature",
                        "Heat as a dial, not a rule",
                        "Ninety-three degrees Celsius is a useful starting point for light roasts: hot enough to extract soluble compounds quickly, cool enough to avoid scorching delicate acids. Darker roasts prefer lower temperatures — ninety to ninety-one — because their cellular structure is more porous and over-extraction arrives faster. The math is simple; the feel takes years.",
                        "Too hot and the cup turns bitter before you finish pouring. Too cool and sweetness never arrives."),
                    new(
                        null,
                        "Mineral balance",
                        "Hard water, soft cups — distilled water produces a thin, hollow cup because it lacks the calcium and magnesium that help extract flavor compounds. Overly hard water can mute acidity and leave scale in your kettle. We brew with filtered water adjusted to roughly 150 ppm total dissolved solids — enough structure to carry sweetness, not so much that brightness disappears."),
                    new(
                        "Brew Bar",
                        null,
                        "Our kettle is set to 93°C each morning and checked with a simple probe thermometer. The number goes on a chalk strip beside the grinder setting. Small rituals of measurement, repeated daily, are how a cafe keeps its cups honest without turning the bar into a laboratory.",
                        IsAside: true),
                    new(
                        "Technique",
                        "The patient pour",
                        "A spiral pour is not about aesthetics. It is about even saturation — water meeting every particle of ground coffee at roughly the same rate. Pour too fast and channels form: water finds the path of least resistance and bypasses most of the bed. Pour too slow and the slurry cools before extraction finishes. The sweet spot is a steady, thin stream that sounds like rain on a tin roof.",
                        "Listen to the pour. The right rhythm has a sound you can learn to recognize."),
                    new(
                        null,
                        "Before the first sip",
                        "By the time the drawdown completes, most of the math has already happened invisibly. Temperature chose which compounds to release. Minerals decided how they would land on your palate. Pour rhythm determined whether sweetness would arrive evenly or in a single sharp note. The first sip is just the receipt.",
                        null)
                ],
                "In our next brew note: spiral geometry on the V60, and the small corrections worth making mid-pour.")
        };

    public static bool TryGetBody(string slug, out JournalArticleBody body) =>
        Bodies.TryGetValue(slug, out body!);

    public static IReadOnlyList<JournalArticle> RelatedArticles(string currentSlug, int count = 2)
    {
        return JournalCatalog.HomeArticles
            .Where(article => !string.Equals(article.Slug, currentSlug, StringComparison.OrdinalIgnoreCase))
            .Take(count)
            .ToArray();
    }
}
