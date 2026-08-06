namespace ThisCafeteria.Domain.Entities;

public sealed class Coupon
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = string.Empty;
    public string NormalizedCode { get; private set; } = string.Empty;
    public decimal DiscountPercent { get; private set; }
    public decimal MinimumOrderTotal { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    public List<CouponRedemption> Redemptions { get; set; } = [];

    private Coupon() { }

    public static Coupon Create(string code, decimal discountPercent, decimal minimumOrderTotal, DateTime? createdAtUtc = null)
    {
        var coupon = new Coupon { CreatedAt = createdAtUtc ?? DateTime.UtcNow };
        coupon.Update(code, discountPercent, minimumOrderTotal, isActive: true, updatedAtUtc: null);
        return coupon;
    }

    public void Update(string code, decimal discountPercent, decimal minimumOrderTotal, bool isActive, DateTime? updatedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(code) || discountPercent is <= 0m or > 100m || minimumOrderTotal < 0m)
        {
            throw new InvalidOperationException("Coupon terms are invalid.");
        }

        Code = code.Trim();
        NormalizedCode = NormalizeCode(code);
        DiscountPercent = discountPercent;
        MinimumOrderTotal = minimumOrderTotal;
        IsActive = isActive;
        UpdatedAt = updatedAtUtc;
    }

    public bool CanBeRedeemedFor(decimal orderTotal) => IsActive && orderTotal >= MinimumOrderTotal;

    public void Deactivate(DateTime? updatedAtUtc = null)
    {
        IsActive = false;
        UpdatedAt = updatedAtUtc ?? DateTime.UtcNow;
    }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}
