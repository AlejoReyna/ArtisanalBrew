using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services;

public interface IOrderPricingService
{
    decimal ShippingUsd { get; }
    OrderPricingDto Calculate(IReadOnlyCollection<CartItemDto> items, Coupon? coupon = null);
}
