namespace ThisCafeteria.Domain.Entities;

public sealed class Coupon
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string NormalizedCode { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public decimal MinimumOrderTotal { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<CouponRedemption> Redemptions { get; set; } = [];
}
