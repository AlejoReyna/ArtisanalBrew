using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services;

public sealed class OrderPricingService : IOrderPricingService
{
    private const decimal TaxRate = 0.16m;
    private const decimal ShippingAmount = 4.00m;

    public decimal ShippingUsd => ShippingAmount;

    public OrderPricingDto Calculate(IReadOnlyCollection<CartItemDto> items, Coupon? coupon = null)
    {
        var subtotal = items.Sum(item => item.UnitPrice * item.Quantity);
        var tax = decimal.Round(subtotal * TaxRate, 2, MidpointRounding.AwayFromZero);
        var totalBeforeDiscount = subtotal + ShippingAmount + tax;
        var discountAmount = coupon is null
            ? 0m
            : decimal.Round(totalBeforeDiscount * coupon.DiscountPercent / 100m, 2, MidpointRounding.AwayFromZero);
        var total = Math.Max(0m, totalBeforeDiscount - discountAmount);

        return new OrderPricingDto(
            subtotal,
            ShippingAmount,
            tax,
            totalBeforeDiscount,
            coupon?.Code,
            coupon?.DiscountPercent,
            discountAmount,
            total);
    }
}
