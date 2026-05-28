using FluentValidation;
using ThisCafeteria.Application.DTOs;

namespace ThisCafeteria.Application.Validation;

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.UserProfileId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty();
        RuleFor(x => x.WalletAddress)
            .NotEmpty()
            .Length(42)
            .Must(address => address.StartsWith("0x", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.PaymentTransactionHash)
            .NotEmpty()
            .Length(66)
            .Must(hash => hash.StartsWith("0x", StringComparison.OrdinalIgnoreCase));
        RuleFor(x => x.PaymentChainId).GreaterThan(0);
        RuleFor(x => x.PaymentNetworkName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.PaymentEthAmount).GreaterThan(0);
        RuleFor(x => x.PaymentExplorerUrl).NotEmpty().MaximumLength(2_048);
        RuleFor(x => x.PaidAtUtc).NotEmpty();
        RuleFor(x => x.CouponCode)
            .MaximumLength(64)
            .Matches("^[A-Za-z0-9_-]+$")
            .When(x => !string.IsNullOrWhiteSpace(x.CouponCode))
            .WithMessage("Coupon code can only contain letters, numbers, underscores, and hyphens.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.ProductId).NotEmpty();
            item.RuleFor(x => x.ProductName).NotEmpty().MaximumLength(160);
            item.RuleFor(x => x.Quantity).GreaterThan(0);
            item.RuleFor(x => x.UnitPrice).GreaterThan(0);
        });
    }
}
