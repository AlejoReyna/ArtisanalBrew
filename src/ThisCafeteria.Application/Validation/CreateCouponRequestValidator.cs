using FluentValidation;
using ThisCafeteria.Application.DTOs;

namespace ThisCafeteria.Application.Validation;

public sealed class CreateCouponRequestValidator : AbstractValidator<CreateCouponRequest>
{
    public CreateCouponRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("Coupon code can only contain letters, numbers, underscores, and hyphens.");
        RuleFor(x => x.DiscountPercent).GreaterThan(0).LessThanOrEqualTo(100);
        RuleFor(x => x.MinimumOrderTotal).GreaterThanOrEqualTo(0);
    }
}
