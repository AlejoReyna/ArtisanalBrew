using FluentAssertions;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Validation;
using ThisCafeteria.Domain.Avatars;

namespace ThisCafeteria.UnitTests;

public sealed class UpdateAvatarRequestValidatorTests
{
    private readonly UpdateAvatarRequestValidator _validator = new();

    private static UpdateAvatarRequest Valid() =>
        new("night", "moss", "apron", "happy", "toque", "mug");

    [Fact]
    public void AcceptsALookBuiltEntirelyFromTheCatalog()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void AcceptsNoneOnEverySlotThatOffersIt()
    {
        var bare = Valid() with
        {
            Wear = AvatarCatalog.NoneItemId,
            Hat = AvatarCatalog.NoneItemId,
            Hold = AvatarCatalog.NoneItemId
        };

        _validator.Validate(bare).IsValid.Should().BeTrue();
    }

    [Fact]
    public void RejectsNoneOnASlotTheRobotCannotGoWithout()
    {
        // There is no such thing as a robot with no chassis; "none" is only an
        // item on the optional slots, so it must fail here like any other
        // unknown id rather than storing an unrenderable layer.
        var headless = Valid() with { Chassis = AvatarCatalog.NoneItemId };

        _validator.Validate(headless).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Backdrop")]
    [InlineData("Chassis")]
    [InlineData("Wear")]
    [InlineData("Visor")]
    [InlineData("Hat")]
    [InlineData("Hold")]
    public void RejectsAnUnknownIdOnAnySlot(string slotProperty)
    {
        var request = Valid();
        request = slotProperty switch
        {
            "Backdrop" => request with { Backdrop = "not-a-backdrop" },
            "Chassis" => request with { Chassis = "not-a-chassis" },
            "Wear" => request with { Wear = "not-a-uniform" },
            "Visor" => request with { Visor = "not-a-visor" },
            "Hat" => request with { Hat = "not-a-hat" },
            _ => request with { Hold = "not-an-item" }
        };

        var result = _validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(slotProperty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsABlankSlot(string blank)
    {
        _validator.Validate(Valid() with { Hat = blank }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void NamesTheSlotInTheMessageSoTheEditorCanSayWhatWentWrong()
    {
        var result = _validator.Validate(Valid() with { Hat = "sombrero-de-charro" });

        result.Errors.Single().ErrorMessage.Should().Contain("Headgear");
    }
}
