using System.Linq.Expressions;
using FluentValidation;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Domain.Avatars;

namespace ThisCafeteria.Application.Validation;

/// <summary>
/// Rejects anything the catalog does not recognise.
/// </summary>
/// <remarks>
/// The write path is strict where the read path is forgiving, and that
/// asymmetry is deliberate. A stored id that has since been retired is history
/// we cannot change, so reads repair it silently; an unknown id arriving now is
/// either a stale client or someone poking the endpoint, and accepting it would
/// persist a layer that can never render.
/// </remarks>
public sealed class UpdateAvatarRequestValidator : AbstractValidator<UpdateAvatarRequest>
{
    public UpdateAvatarRequestValidator()
    {
        Slot(request => request.Backdrop, AvatarCatalog.BackdropSlot);
        Slot(request => request.Chassis, AvatarCatalog.ChassisSlot);
        Slot(request => request.Wear, AvatarCatalog.WearSlot);
        Slot(request => request.Visor, AvatarCatalog.VisorSlot);
        Slot(request => request.Hat, AvatarCatalog.HatSlot);
        Slot(request => request.Hold, AvatarCatalog.HoldSlot);
    }

    private void Slot(Expression<Func<UpdateAvatarRequest, string>> selector, string slotKey)
    {
        var slot = AvatarCatalog.GetSlot(slotKey);

        RuleFor(selector)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(itemId => slot.Contains(itemId))
            .WithMessage($"'{{PropertyValue}}' is not one of the available {slot.Label} options.");
    }
}
