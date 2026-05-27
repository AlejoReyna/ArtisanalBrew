using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Domain.Enums;
using ThisCafeteria.Infrastructure.Configuration;

namespace ThisCafeteria.Infrastructure.Persistence;

public sealed class DatabaseSeeder(AppDbContext dbContext, IOptions<CatalogOptions> catalogOptions)
{
    private static readonly IReadOnlyList<CatalogSeedProduct> CatalogProducts =
    [
        new("Ethiopia Yirgacheffe", ProductCategory.Beans, 29.60m),
        new("Colombia Huila", ProductCategory.Beans, 28.20m),
        new("Guatemala Antigua", ProductCategory.Beans, 27.40m),
        new("Kenya Nyeri AA", ProductCategory.Beans, 31.20m),
        new("Costa Rica Tarrazu", ProductCategory.Beans, 30.40m),
        new("Brazil Cerrado", ProductCategory.Beans, 24.80m),
        new("Rwanda Huye Mountain", ProductCategory.Beans, 30.80m),
        new("Peru Cajamarca", ProductCategory.Beans, 26.40m),
        new("Mexico Chiapas", ProductCategory.Beans, 25.20m),
        new("Panama Boquete", ProductCategory.Beans, 32.00m),
        new("Ceramic Pour-Over Dripper", ProductCategory.BrewingEquipment, 58.00m),
        new("Gooseneck Kettle", ProductCategory.BrewingEquipment, 96.00m),
        new("Burr Hand Grinder", ProductCategory.BrewingEquipment, 128.00m),
        new("Digital Coffee Scale", ProductCategory.BrewingEquipment, 64.00m),
        new("Cold Brew Tower", ProductCategory.BrewingEquipment, 165.00m),
        new("French Press", ProductCategory.BrewingEquipment, 54.00m),
        new("AeroPress Kit", ProductCategory.BrewingEquipment, 48.00m),
        new("Reusable Metal Filter", ProductCategory.BrewingEquipment, 24.00m),
        new("Espresso Tamper", ProductCategory.BrewingEquipment, 42.00m),
        new("Milk Frothing Pitcher", ProductCategory.BrewingEquipment, 32.00m),
        new("Stoneware Latte Cup", ProductCategory.CeramicsAndGoods, 34.00m),
        new("Handmade Espresso Cup", ProductCategory.CeramicsAndGoods, 28.00m),
        new("Stacking Mug Set", ProductCategory.CeramicsAndGoods, 72.00m),
        new("Minimal Travel Tumbler", ProductCategory.CeramicsAndGoods, 38.00m),
        new("Linen Cafe Tote", ProductCategory.CeramicsAndGoods, 26.00m),
        new("Walnut Serving Tray", ProductCategory.CeramicsAndGoods, 88.00m),
        new("Porcelain Cupping Bowl", ProductCategory.CeramicsAndGoods, 22.00m),
        new("Speckled Sugar Jar", ProductCategory.CeramicsAndGoods, 30.00m),
        new("Cotton Barista Apron", ProductCategory.CeramicsAndGoods, 62.00m),
        new("Brass Coffee Scoop", ProductCategory.CeramicsAndGoods, 24.00m)
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);

        if (catalogOptions.Value.SeedProductsOnStartup)
        {
            await SeedCatalogProductsAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
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
        seed.Category == ProductCategory.Beans
            ? $"{seed.Name} is a single origin bean selection ready for checkout."
            : $"{seed.Name} is a marketplace good for the cafe ritual.";

    private static string SlugFor(string name) =>
        string.Join('-', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private sealed record CatalogSeedProduct(string Name, ProductCategory Category, decimal Price);
}
