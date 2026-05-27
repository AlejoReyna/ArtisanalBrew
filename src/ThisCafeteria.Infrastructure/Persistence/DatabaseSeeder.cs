using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Infrastructure.Persistence;

public sealed class DatabaseSeeder(AppDbContext dbContext)
{
    private static readonly IReadOnlyList<CatalogSeedProduct> CatalogProducts =
    [
        new("Ethiopia Yirgacheffe", ProductCategory.Coffee, 29.60m),
        new("Colombia Huila", ProductCategory.Coffee, 28.20m),
        new("Guatemala Antigua", ProductCategory.Coffee, 27.40m),
        new("Kenya Nyeri AA", ProductCategory.Coffee, 31.20m),
        new("Costa Rica Tarrazu", ProductCategory.Coffee, 30.40m),
        new("Brazil Cerrado", ProductCategory.Coffee, 24.80m),
        new("Rwanda Huye Mountain", ProductCategory.Coffee, 30.80m),
        new("Peru Cajamarca", ProductCategory.Coffee, 26.40m),
        new("Mexico Chiapas", ProductCategory.Coffee, 25.20m),
        new("Panama Boquete", ProductCategory.Coffee, 32.00m),
        new("Ceramic Pour-Over Dripper", ProductCategory.Merchandise, 58.00m),
        new("Gooseneck Kettle", ProductCategory.Merchandise, 96.00m),
        new("Burr Hand Grinder", ProductCategory.Merchandise, 128.00m),
        new("Digital Coffee Scale", ProductCategory.Merchandise, 64.00m),
        new("Cold Brew Tower", ProductCategory.Merchandise, 165.00m),
        new("French Press", ProductCategory.Merchandise, 54.00m),
        new("AeroPress Kit", ProductCategory.Merchandise, 48.00m),
        new("Reusable Metal Filter", ProductCategory.Merchandise, 24.00m),
        new("Espresso Tamper", ProductCategory.Merchandise, 42.00m),
        new("Milk Frothing Pitcher", ProductCategory.Merchandise, 32.00m),
        new("Stoneware Latte Cup", ProductCategory.Merchandise, 34.00m),
        new("Handmade Espresso Cup", ProductCategory.Merchandise, 28.00m),
        new("Stacking Mug Set", ProductCategory.Merchandise, 72.00m),
        new("Minimal Travel Tumbler", ProductCategory.Merchandise, 38.00m),
        new("Linen Cafe Tote", ProductCategory.Merchandise, 26.00m),
        new("Walnut Serving Tray", ProductCategory.Merchandise, 88.00m),
        new("Porcelain Cupping Bowl", ProductCategory.Merchandise, 22.00m),
        new("Speckled Sugar Jar", ProductCategory.Merchandise, 30.00m),
        new("Cotton Barista Apron", ProductCategory.Merchandise, 62.00m),
        new("Brass Coffee Scoop", ProductCategory.Merchandise, 24.00m)
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
        await SeedCatalogProductsAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedCatalogProductsAsync(CancellationToken cancellationToken)
    {
        var existingSlugs = await dbContext.Products
            .Select(product => product.Slug)
            .ToArrayAsync(cancellationToken);
        var existing = existingSlugs.ToHashSet(StringComparer.Ordinal);
        var createdAt = DateTime.UtcNow;

        foreach (var seed in CatalogProducts)
        {
            var slug = SlugFor(seed.Name);
            if (existing.Contains(slug))
            {
                continue;
            }

            dbContext.Products.Add(new Product
            {
                Name = seed.Name,
                Slug = slug,
                Description = DescriptionFor(seed),
                Price = seed.Price,
                StockQuantity = 100,
                Category = seed.Category,
                IsActive = true,
                CreatedAt = createdAt
            });
        }
    }

    private static string DescriptionFor(CatalogSeedProduct seed) =>
        seed.Category == ProductCategory.Coffee
            ? $"{seed.Name} is a marketplace coffee selection ready for checkout."
            : $"{seed.Name} is a marketplace good for the cafe ritual.";

    private static string SlugFor(string name) =>
        string.Join('-', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private sealed record CatalogSeedProduct(string Name, ProductCategory Category, decimal Price);
}
