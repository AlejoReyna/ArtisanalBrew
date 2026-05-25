using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(order => order.Id);
        builder.Property(order => order.OrderNumber).HasMaxLength(64).IsRequired();
        builder.HasIndex(order => order.OrderNumber).IsUnique();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(80);
        builder.Property(order => order.Subtotal).HasPrecision(18, 2);
        builder.Property(order => order.Tax).HasPrecision(18, 2);
        builder.Property(order => order.Total).HasPrecision(18, 2);

        builder.HasOne(order => order.UserProfile)
            .WithMany(user => user.Orders)
            .HasForeignKey(order => order.UserProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(order => order.Items)
            .WithOne(item => item.Order)
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
