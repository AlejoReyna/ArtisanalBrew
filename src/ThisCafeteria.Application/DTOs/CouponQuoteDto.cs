namespace ThisCafeteria.Application.DTOs;

public sealed record CouponQuoteDto(
    CouponDto Coupon,
    OrderPricingDto Pricing);
