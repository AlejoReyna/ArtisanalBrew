namespace ThisCafeteria.Application.DTOs;

public sealed record CouponDto(
    Guid Id,
    string Code,
    decimal DiscountPercent,
    decimal MinimumOrderTotal,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
