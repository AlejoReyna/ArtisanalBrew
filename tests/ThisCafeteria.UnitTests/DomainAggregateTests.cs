using FluentAssertions;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.UnitTests;

public sealed class DomainAggregateTests
{
    [Fact]
    public void OrderPricing_CalculatesTaxAndDiscountFromOrderItems()
    {
        var pricing = OrderPricing.Calculate(
            [new OrderItem { Quantity = 2, UnitPrice = 10m }],
            shippingAmount: 4m,
            taxRate: 0.16m,
            discountPercent: 10m);

        pricing.Subtotal.Should().Be(20m);
        pricing.Tax.Should().Be(3.20m);
        pricing.DiscountAmount.Should().Be(2.72m);
        pricing.Total.Should().Be(24.48m);
    }

    [Fact]
    public void Order_OnlyPermitsForwardLifecycleTransitions()
    {
        var order = Order.Place(
            Guid.NewGuid(), "TC-1", "0xwallet",
            [new OrderItem { Quantity = 1, UnitPrice = 10m }],
            OrderPricing.Calculate([new OrderItem { Quantity = 1, UnitPrice = 10m }], 4m, 0.16m),
            coupon: null);

        order.TransitionTo(OrderStatus.Completed);

        order.Status.Should().Be(OrderStatus.Completed);
        FluentActions.Invoking(() => order.TransitionTo(OrderStatus.Processing))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Coupon_EncapsulatesTermsAndEligibility()
    {
        var coupon = Coupon.Create(" welcome10 ", 10m, 25m);

        coupon.Code.Should().Be("welcome10");
        coupon.NormalizedCode.Should().Be("WELCOME10");
        coupon.CanBeRedeemedFor(24.99m).Should().BeFalse();
        coupon.CanBeRedeemedFor(25m).Should().BeTrue();

        coupon.Deactivate();
        coupon.CanBeRedeemedFor(100m).Should().BeFalse();
    }

    [Fact]
    public void StakingOperationIdentity_RejectsIncompleteOrNegativeKeys()
    {
        FluentActions.Invoking(() => StakingOperationIdentity.Create("chain", "tx", -1))
            .Should().Throw<InvalidOperationException>();

        var identity = StakingOperationIdentity.Create("chain", "tx", 2);
        identity.Should().Be(new StakingOperationIdentity("chain", "tx", 2));
    }
}
