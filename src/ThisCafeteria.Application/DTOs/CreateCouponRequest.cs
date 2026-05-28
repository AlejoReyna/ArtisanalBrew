namespace ThisCafeteria.Application.DTOs;

public sealed record CreateCouponRequest(
    string Code,
    decimal DiscountPercent,
    decimal MinimumOrderTotal);
