using ThisCafeteria.Domain.Avatars;

namespace ThisCafeteria.Application.DTOs;

/// <summary>
/// A robot ready to render: six catalog item ids, guaranteed to exist.
/// </summary>
/// <remarks>
/// Slots are declared in layer order, back to front, so a renderer can walk
/// the record top to bottom and stack as it goes.
/// </remarks>
public sealed record RobotAvatarDto(
    string Backdrop,
    string Chassis,
    string Wear,
    string Visor,
    string Hat,
    string Hold)
{
    /// <summary>
    /// The read path, in one place.
    /// </summary>
    /// <remarks>
    /// Every route from the database to a screen goes through here, which is
    /// what makes two rules unskippable: a profile that has never been edited
    /// (<paramref name="stored"/> is null) renders from its wallet seed rather
    /// than from a fixed default robot, and an id that has since left the
    /// catalog falls back instead of rendering a broken layer.
    /// </remarks>
    public static RobotAvatarDto Resolve(RobotAvatar? stored, string? walletAddress)
    {
        var look = AvatarCatalog.Normalize(stored ?? AvatarSeed.FromWallet(walletAddress));

        return new RobotAvatarDto(
            look.Backdrop,
            look.Chassis,
            look.Wear,
            look.Visor,
            look.Hat,
            look.Hold);
    }

    /// <summary>Slot-keyed access so a renderer can loop <see cref="AvatarCatalog.Slots"/>.</summary>
    public string this[string slotKey] => slotKey switch
    {
        AvatarCatalog.BackdropSlot => Backdrop,
        AvatarCatalog.ChassisSlot => Chassis,
        AvatarCatalog.WearSlot => Wear,
        AvatarCatalog.VisorSlot => Visor,
        AvatarCatalog.HatSlot => Hat,
        AvatarCatalog.HoldSlot => Hold,
        _ => throw new ArgumentOutOfRangeException(nameof(slotKey), slotKey, "Unknown avatar slot.")
    };
}
