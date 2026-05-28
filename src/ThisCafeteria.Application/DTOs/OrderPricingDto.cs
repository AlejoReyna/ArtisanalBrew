namespace ThisCafeteria.Application.DTOs;

public sealed record OrderPricingDto(
    decimal Subtotal,
    decimal Shipping,
    decimal Tax,
    decimal TotalBeforeDiscount,
    string? CouponCode,
    decimal? CouponDiscountPercent,
    decimal DiscountAmount,
    decimal Total);
