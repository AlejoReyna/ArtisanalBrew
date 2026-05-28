using FluentAssertions;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.UnitTests;

public sealed class OrderPricingServiceTests
{
    [Fact]
    public void Calculate_ShouldApplyCouponToFullOrderTotal()
    {
        var service = new OrderPricingService();
        var items = new[]
        {
            new CartItemDto(Guid.NewGuid(), "Coffee", 2, 10m)
        };
        var coupon = new Coupon
        {
            Code = "WELCOME10",
            DiscountPercent = 10m,
            MinimumOrderTotal = 0m
        };

        var pricing = service.Calculate(items, coupon);

        pricing.Subtotal.Should().Be(20m);
        pricing.Shipping.Should().Be(4m);
        pricing.Tax.Should().Be(3.20m);
        pricing.TotalBeforeDiscount.Should().Be(27.20m);
        pricing.DiscountAmount.Should().Be(2.72m);
        pricing.Total.Should().Be(24.48m);
    }
}
