using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Web.Catalog;

/// <summary>
/// Card view model shaped after the catalog JSON component model:
/// ProductImage, WishlistButton, RatingStars, Brand, ProductName, PriceRow(current, compareAt).
/// </summary>
public sealed record CatalogProductCard(
    string Slug,
    string Name,
    string Brand,
    string? ImageUrl,
    decimal Price,
    decimal? CompareAtPrice,
    int Rating,
    bool IsAvailable,
    string CategoryKey,
    string CategoryLabel);

/// <summary>One selectable row in the filter sidebar category list.</summary>
public sealed record CatalogCategoryOption(string Key, string Label, int Count);

/// <summary>Removable chip in the toolbar summarizing an applied filter.</summary>
public sealed record CatalogFilterChip(string Key, string Label);

public static class CatalogProductCardMapper
{
    public static CatalogProductCard ToCard(ProductDto product)
    {
        var hash = StableHash(product.Slug);
        var compareAt = hash % 3 == 0
            ? Math.Round(product.Price * 1.4m, 0)
            : (decimal?)null;

        return new CatalogProductCard(
            product.Slug,
            product.Name,
            BrandFor(product.Category),
            product.ImageUrl,
            product.Price,
            compareAt,
            Rating: 3 + hash % 3,
            IsAvailable: product.IsActive && product.StockQuantity > 0,
            product.Category.ToString(),
            LabelFor(product.Category));
    }

    public static string BrandFor(ProductCategory category) =>
        category switch
        {
            ProductCategory.Beans => "Single Origin",
            ProductCategory.BrewingEquipment => "Brew Tools",
            ProductCategory.CeramicsAndGoods => "Ceramics & Goods",
            _ => "Artisanal"
        };

    public static string LabelFor(ProductCategory category) =>
        category switch
        {
            ProductCategory.Beans => "Single Origin Beans",
            ProductCategory.BrewingEquipment => "Brewing Equipment",
            ProductCategory.CeramicsAndGoods => "Ceramics & Goods",
            _ => "Coffee & Goods"
        };

    // string.GetHashCode() is randomized per process, which would reshuffle ratings and
    // compare-at prices on every restart. A tiny deterministic hash keeps cards stable.
    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 5381;
            foreach (var c in value)
            {
                hash = (hash * 33) ^ c;
            }

            return hash & 0x7fffffff;
        }
    }
}
