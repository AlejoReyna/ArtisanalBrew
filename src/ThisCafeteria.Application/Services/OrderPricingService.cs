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
        var pricing = OrderPricing.Calculate(
            items.Select(item => new OrderItem { Quantity = item.Quantity, UnitPrice = item.UnitPrice }),
            ShippingAmount,
            TaxRate,
            coupon?.DiscountPercent);

        return new OrderPricingDto(
            pricing.Subtotal,
            pricing.Shipping,
            pricing.Tax,
            pricing.TotalBeforeDiscount,
            coupon?.Code,
            coupon?.DiscountPercent,
            pricing.DiscountAmount,
            pricing.Total);
    }
}
