using FluentAssertions;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Validation;
using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.UnitTests;

public sealed class CreateProductRequestValidatorTests
{
    [Fact]
    public void Validate_ShouldPass_WhenRequestIsValid()
    {
        var validator = new CreateProductRequestValidator();
        var request = new CreateProductRequest(
            "House Espresso",
            "A double shot with chocolate notes.",
            3.50m,
            10,
            null,
            ProductCategory.Espresso);

        var result = validator.Validate(request);

        result.IsValid.Should().BeTrue();
    }
}
