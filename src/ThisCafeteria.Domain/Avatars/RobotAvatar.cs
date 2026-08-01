namespace ThisCafeteria.Domain.Avatars;

/// <summary>
/// What a wallet's robot is wearing: one catalog item id per slot.
/// </summary>
/// <remarks>
/// This is stored as a single JSON column rather than six columns because the
/// slots are a presentation concern that will keep moving — adding a seventh
/// slot should cost a catalog entry and a sprite sheet column, not a migration.
/// Nothing queries an avatar by its contents.
///
/// The ids here are <em>untrusted</em> even though they came from our own
/// database: a row written before an item was renamed still holds the old id.
/// Never render one without passing it through <see cref="AvatarCatalog.Normalize"/>.
/// </remarks>
public sealed class RobotAvatar
{
    public string Chassis { get; set; } = string.Empty;
    public string Visor { get; set; } = string.Empty;
    public string Hat { get; set; } = string.Empty;
    public string Wear { get; set; } = string.Empty;
    public string Hold { get; set; } = string.Empty;
    public string Backdrop { get; set; } = string.Empty;

    /// <summary>
    /// Slot-keyed access, so the editor and the renderer can loop over
    /// <see cref="AvatarCatalog.Slots"/> instead of hard-coding six branches.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The slot key is not one of the six. Unlike an unknown <em>item id</em>
    /// — which is data and gets a silent fallback — an unknown slot key can
    /// only be a coding mistake, so it fails loudly.
    /// </exception>
    public string this[string slotKey]
    {
        get => slotKey switch
        {
            AvatarCatalog.ChassisSlot => Chassis,
            AvatarCatalog.VisorSlot => Visor,
            AvatarCatalog.HatSlot => Hat,
            AvatarCatalog.WearSlot => Wear,
            AvatarCatalog.HoldSlot => Hold,
            AvatarCatalog.BackdropSlot => Backdrop,
            _ => throw new ArgumentOutOfRangeException(nameof(slotKey), slotKey, "Unknown avatar slot.")
        };
        set
        {
            switch (slotKey)
            {
                case AvatarCatalog.ChassisSlot:
                    Chassis = value;
                    break;
                case AvatarCatalog.VisorSlot:
                    Visor = value;
                    break;
                case AvatarCatalog.HatSlot:
                    Hat = value;
                    break;
                case AvatarCatalog.WearSlot:
                    Wear = value;
                    break;
                case AvatarCatalog.HoldSlot:
                    Hold = value;
                    break;
                case AvatarCatalog.BackdropSlot:
                    Backdrop = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(slotKey), slotKey, "Unknown avatar slot.");
            }
        }
    }

    public RobotAvatar Clone() => new()
    {
        Chassis = Chassis,
        Visor = Visor,
        Hat = Hat,
        Wear = Wear,
        Hold = Hold,
        Backdrop = Backdrop
    };

    public bool HasSameLook(RobotAvatar? other) =>
        other is not null &&
        Chassis == other.Chassis &&
        Visor == other.Visor &&
        Hat == other.Hat &&
        Wear == other.Wear &&
        Hold == other.Hold &&
        Backdrop == other.Backdrop;
}
