using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services;

public interface IOrderPricingService
{
    decimal ShippingUsd { get; }

    /// <summary>
    /// Fixed demo ETH/USD rate used to convert a USD order total into native ETH.
    /// Not an oracle. Checkout refuses any native symbol other than ETH.
    /// </summary>
    decimal EthUsdRate { get; }

    OrderPricingDto Calculate(IReadOnlyCollection<CartItemDto> items, Coupon? coupon = null);

    decimal ToNativePaymentAmount(decimal usdTotal, string nativeCurrencySymbol);
}
