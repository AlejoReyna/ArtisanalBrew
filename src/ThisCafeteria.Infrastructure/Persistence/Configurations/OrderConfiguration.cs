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
        builder.Property(order => order.WalletAddress).HasMaxLength(42).IsRequired();
        builder.Property(order => order.PaymentTransactionHash).HasMaxLength(66);
        builder.Property(order => order.PaymentNetworkName).HasMaxLength(80);
        builder.Property(order => order.PaymentEthAmount).HasPrecision(28, 18);
        builder.Property(order => order.PaymentExplorerUrl).HasMaxLength(2_048);

        builder.HasIndex(order => order.WalletAddress);
        builder.HasIndex(order => order.PaymentTransactionHash)
            .IsUnique()
            .HasFilter("\"PaymentTransactionHash\" IS NOT NULL");

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
