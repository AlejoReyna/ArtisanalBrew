using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(product => product.Id);
        builder.Property(product => product.Name).HasMaxLength(160).IsRequired();
        builder.Property(product => product.Slug).HasMaxLength(180).IsRequired();
        builder.HasIndex(product => product.Slug).IsUnique();
        builder.Property(product => product.Description).HasMaxLength(1_000).IsRequired();
        builder.Property(product => product.Price).HasPrecision(18, 2);
        builder.Property(product => product.ImageUrl).HasMaxLength(2_048);
        builder.Property(product => product.Category).HasConversion<string>().HasMaxLength(80);
    }
}
