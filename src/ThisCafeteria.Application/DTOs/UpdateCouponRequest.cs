namespace ThisCafeteria.Application.DTOs;

public sealed record UpdateCouponRequest(
    string Code,
    decimal DiscountPercent,
    decimal MinimumOrderTotal,
    bool IsActive);
