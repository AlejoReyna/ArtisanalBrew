using ThisCafeteria.Application.DTOs;

namespace ThisCafeteria.Web.Catalog;

/// <summary>
/// Resolves the photography a product shows in the grid and on its detail page.
///
/// Seeded rows can point at /images/products/*.jpg assets that are not in wwwroot, and most
/// rows carry no ImageUrl at all, so every surface needs the same deterministic remote
/// fallback — otherwise the shared-element flight from a grid card cross-fades into a
/// different photo on the detail page.
/// </summary>
public static class CatalogImagery
{
    /// <summary>
    /// The first four entries are the grid's original pool and their order is load-bearing:
    /// <see cref="Primary"/> indexes into just those four, so extending the list below cannot
    /// change which photo an existing product card shows.
    /// </summary>
    private static readonly string[] FallbackImages =
    [
        "https://images.unsplash.com/photo-1447933601403-0c6688de566e?auto=format&fit=crop&w=1200&q=80",
        "https://images.unsplash.com/photo-1544787219-7f47ccb76574?auto=format&fit=crop&w=1200&q=80",
        "https://images.unsplash.com/photo-1572119865084-43c285814d63?auto=format&fit=crop&w=1200&q=80",
        "https://images.unsplash.com/photo-1495474472287-4d71bcdd2085?auto=format&fit=crop&w=1200&q=80",
        "https://images.unsplash.com/photo-1461023058943-07fcbe16d735?auto=format&fit=crop&w=1200&q=80",
        "https://images.unsplash.com/photo-1509042239860-f550ce710b93?auto=format&fit=crop&w=1200&q=80",
        "https://images.unsplash.com/photo-1521302080334-4bebac2763a6?auto=format&fit=crop&w=1200&q=80",
        "https://images.unsplash.com/photo-1514432324607-a09d9b4aefdd?auto=format&fit=crop&w=1200&q=80"
    ];

    private const int PrimaryPoolSize = 4;

    /// <summary>The single photo used wherever a product is shown once (grid card, cart, hero).</summary>
    public static string Primary(ProductDto product, string? webRootPath) =>
        OwnImage(product, webRootPath) ?? FallbackImages[PrimaryIndex(product)];

    /// <summary>
    /// The detail page's thumbnail rail: the primary photo first, then further frames rotating
    /// through the rest of the pool. Deterministic per slug, so the rail is stable across renders.
    /// </summary>
    public static IReadOnlyList<string> Gallery(ProductDto product, string? webRootPath, int count = 5)
    {
        var primaryIndex = PrimaryIndex(product);
        var gallery = new List<string>(count) { Primary(product, webRootPath) };

        for (var step = 1; step < FallbackImages.Length && gallery.Count < count; step++)
        {
            var candidate = FallbackImages[(primaryIndex + step) % FallbackImages.Length];

            if (!gallery.Contains(candidate, StringComparer.Ordinal))
            {
                gallery.Add(candidate);
            }
        }

        return gallery;
    }

    private static int PrimaryIndex(ProductDto product) => product.Slug.Sum(c => c) % PrimaryPoolSize;

    private static string? OwnImage(ProductDto product, string? webRootPath)
    {
        if (string.IsNullOrWhiteSpace(product.ImageUrl))
        {
            return null;
        }

        if (product.ImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return product.ImageUrl;
        }

        var relativePath = product.ImageUrl.TrimStart('/');
        return File.Exists(Path.Combine(webRootPath ?? string.Empty, relativePath))
            ? product.ImageUrl
            : null;
    }
}
