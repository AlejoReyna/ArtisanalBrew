using System.Text.RegularExpressions;

namespace ThisCafeteria.Application.Common;

internal static partial class SlugGenerator
{
    public static string Create(string value)
    {
        var slug = value.Trim().ToLowerInvariant();
        slug = InvalidCharacters().Replace(slug, "-");
        slug = RepeatedHyphens().Replace(slug, "-");
        return slug.Trim('-');
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex InvalidCharacters();

    [GeneratedRegex("-+")]
    private static partial Regex RepeatedHyphens();
}
