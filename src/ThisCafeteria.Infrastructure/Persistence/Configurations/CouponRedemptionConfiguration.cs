using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
{
    public void Configure(EntityTypeBuilder<CouponRedemption> builder)
    {
        builder.HasKey(redemption => redemption.Id);

        builder.HasIndex(redemption => new { redemption.CouponId, redemption.UserProfileId }).IsUnique();
        builder.HasIndex(redemption => redemption.OrderId).IsUnique();

        builder.HasOne(redemption => redemption.Coupon)
            .WithMany(coupon => coupon.Redemptions)
            .HasForeignKey(redemption => redemption.CouponId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(redemption => redemption.UserProfile)
            .WithMany(user => user.CouponRedemptions)
            .HasForeignKey(redemption => redemption.UserProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(redemption => redemption.Order)
            .WithOne(order => order.CouponRedemption)
            .HasForeignKey<CouponRedemption>(redemption => redemption.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
