using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services;

public sealed class OrderPricingService : IOrderPricingService
{
    private const decimal TaxRate = 0.16m;
    private const decimal ShippingAmount = 4.00m;
    private const decimal DemoEthUsdRate = 3750.00m;

    public decimal ShippingUsd => ShippingAmount;

    public decimal EthUsdRate => DemoEthUsdRate;

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

    public decimal ToNativePaymentAmount(decimal usdTotal, string nativeCurrencySymbol)
    {
        if (usdTotal <= 0m)
        {
            throw new InvalidOperationException("A payable order total is required.");
        }

        // The storefront still prices in USD-per-ETH. Converting that number into any other
        // native asset would charge the wrong amount (see docs/bsc-testnet-marketplace-follow-up.md).
        if (!string.Equals(nativeCurrencySymbol, "ETH", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Checkout pricing is not yet available in the selected network's native currency.");
        }

        return decimal.Round(usdTotal / DemoEthUsdRate, 18, MidpointRounding.ToZero);
    }
}
