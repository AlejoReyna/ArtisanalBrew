using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Infrastructure.Persistence;

internal static class SeedData
{
    public static readonly Guid EspressoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid LatteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid ColdBrewId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static void Configure(ModelBuilder builder)
    {
        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        builder.Entity<Product>().HasData(
            new Product
            {
                Id = EspressoId,
                Name = "House Espresso",
                Slug = "house-espresso",
                Description = "A bright double shot with chocolate notes.",
                Price = 3.50m,
                StockQuantity = 100,
                ImageUrl = "/images/products/house-espresso.jpg",
                Category = ProductCategory.Espresso,
                IsActive = true,
                CreatedAt = createdAt
            },
            new Product
            {
                Id = LatteId,
                Name = "Vanilla Cloud Latte",
                Slug = "vanilla-cloud-latte",
                Description = "Steamed milk, espresso, and a light vanilla finish.",
                Price = 5.75m,
                StockQuantity = 80,
                ImageUrl = "/images/products/vanilla-cloud-latte.jpg",
                Category = ProductCategory.Latte,
                IsActive = true,
                CreatedAt = createdAt
            },
            new Product
            {
                Id = ColdBrewId,
                Name = "Midnight Cold Brew",
                Slug = "midnight-cold-brew",
                Description = "Slow-steeped cold brew with a smooth roasted profile.",
                Price = 4.95m,
                StockQuantity = 60,
                ImageUrl = "/images/products/midnight-cold-brew.jpg",
                Category = ProductCategory.Coffee,
                IsActive = true,
                CreatedAt = createdAt
            });
    }
}
