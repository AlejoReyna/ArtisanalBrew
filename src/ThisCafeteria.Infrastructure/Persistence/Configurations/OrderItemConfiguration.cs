using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.ProductName).HasMaxLength(160).IsRequired();
        builder.Property(item => item.UnitPrice).HasPrecision(18, 2);
        builder.Property(item => item.Total).HasPrecision(18, 2);
        builder.Property(item => item.ProductId).IsRequired();

        // Marketplace checkout stores a stable catalog id per slug; those ids are not rows in Products.
        builder.Ignore(item => item.Product);
    }
}
