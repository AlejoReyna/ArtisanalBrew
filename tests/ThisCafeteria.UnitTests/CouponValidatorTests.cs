using FluentAssertions;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Validation;

namespace ThisCafeteria.UnitTests;

public sealed class CouponValidatorTests
{
    [Fact]
    public void Validate_ShouldPass_WhenCreateCouponIsValid()
    {
        var validator = new CreateCouponRequestValidator();
        var request = new CreateCouponRequest("WELCOME10", 10m, 25m);

        var result = validator.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100.01)]
    public void Validate_ShouldFail_WhenDiscountPercentIsOutOfRange(decimal discountPercent)
    {
        var validator = new CreateCouponRequestValidator();
        var request = new CreateCouponRequest("WELCOME10", discountPercent, 25m);

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
    }
}
