using FluentValidation;
using ThisCafeteria.Application.DTOs;

namespace ThisCafeteria.Application.Validation;

public sealed class UpdateUserProfileRequestValidator : AbstractValidator<UpdateUserProfileRequest>
{
    public UpdateUserProfileRequestValidator()
    {
        RuleFor(request => request.DisplayName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(displayName => displayName.Trim().Length == displayName.Length)
            .WithMessage("Display name cannot include leading or trailing whitespace.")
            .Length(2, 160)
            .Must(displayName => !ContainsHtmlTag(displayName))
            .WithMessage("Display name cannot contain HTML tags.");
    }

    private static bool ContainsHtmlTag(string value) =>
        value.Contains('<', StringComparison.Ordinal) || value.Contains('>', StringComparison.Ordinal);
}
