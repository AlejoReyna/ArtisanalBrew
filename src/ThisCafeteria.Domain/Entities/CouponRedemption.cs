namespace ThisCafeteria.Domain.Entities;

public sealed class CouponRedemption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CouponId { get; set; }
    public Guid UserProfileId { get; set; }
    public Guid OrderId { get; set; }
    public DateTime RedeemedAtUtc { get; set; } = DateTime.UtcNow;

    public Coupon? Coupon { get; set; }
    public UserProfile? UserProfile { get; set; }
    public Order? Order { get; set; }
}
