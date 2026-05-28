using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.HasKey(coupon => coupon.Id);
        builder.Property(coupon => coupon.Code).HasMaxLength(64).IsRequired();
        builder.Property(coupon => coupon.NormalizedCode).HasMaxLength(64).IsRequired();
        builder.Property(coupon => coupon.DiscountPercent).HasPrecision(5, 2);
        builder.Property(coupon => coupon.MinimumOrderTotal).HasPrecision(18, 2);

        builder.HasIndex(coupon => coupon.NormalizedCode).IsUnique();
    }
}
