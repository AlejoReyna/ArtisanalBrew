using System.Collections.Frozen;

namespace ThisCafeteria.Domain.Avatars;

/// <summary>
/// How a slot's items reach the screen.
/// </summary>
public enum AvatarSlotKind
{
    /// <summary>One horizontal 64px sprite sheet per slot, indexed by <see cref="AvatarItem.SheetIndex"/>.</summary>
    Sprite,

    /// <summary>No sprite; the item id keys a CSS colour behind the robot.</summary>
    Colour
}

/// <summary>
/// One dressable option.
/// </summary>
/// <param name="Id">Stable storage key. Renaming one orphans every avatar wearing it.</param>
/// <param name="Label">Editor caption.</param>
/// <param name="SheetIndex">
/// Column in the slot's sprite sheet, or <c>-1</c> for an item that draws
/// nothing (the "none" entry on optional slots). Declared explicitly rather
/// than inferred from list position so that a "none" entry sitting first in
/// the list cannot silently shift every sprite one column to the left.
/// </param>
public sealed record AvatarItem(string Id, string Label, int SheetIndex)
{
    public bool IsEmpty => SheetIndex < 0;
}

/// <summary>
/// One layer of the composed robot.
/// </summary>
public sealed record AvatarSlot(
    string Key,
    string Label,
    AvatarSlotKind Kind,
    string DefaultItemId,
    IReadOnlyList<AvatarItem> Items)
{
    /// <summary>Columns in this slot's sheet — what CSS needs for <c>background-size</c>.</summary>
    public int SheetFrames { get; } = Items.Count(item => !item.IsEmpty);

    public AvatarItem Default { get; } =
        Items.FirstOrDefault(item => item.Id == DefaultItemId)
        ?? throw new InvalidOperationException(
            $"Avatar slot '{Key}' names default item '{DefaultItemId}', which is not one of its items.");

    public AvatarItem Resolve(string? itemId) =>
        itemId is not null && _byId.TryGetValue(itemId, out var item) ? item : Default;

    public bool Contains(string? itemId) => itemId is not null && _byId.ContainsKey(itemId);

    private readonly FrozenDictionary<string, AvatarItem> _byId =
        Items.ToFrozenDictionary(item => item.Id, StringComparer.Ordinal);
}

/// <summary>
/// The single source of truth for what a robot can wear.
/// </summary>
/// <remarks>
/// Three consumers read this and they must not drift: the sprite generator
/// (<c>tools/generate_avatar_parts.py</c>) draws the sheet columns in
/// <see cref="AvatarItem.SheetIndex"/> order, the editor builds its swatch
/// grid from <see cref="Slots"/>, and <see cref="Normalize"/> guards the read
/// path. Adding an item means one entry here plus one column in the sheet.
/// </remarks>
public static class AvatarCatalog
{
    public const string ChassisSlot = "chassis";
    public const string VisorSlot = "visor";
    public const string HatSlot = "hat";
    public const string WearSlot = "wear";
    public const string HoldSlot = "hold";
    public const string BackdropSlot = "backdrop";

    /// <summary>The id every optional slot uses for "wearing nothing here".</summary>
    public const string NoneItemId = "none";

    /// <summary>Every sprite sheet is this many pixels per frame, square.</summary>
    public const int FrameSize = 64;

    /// <summary>
    /// Layer order, back to front — also the order the editor lists its tabs.
    /// The held item is last because the robot's hands are drawn in front of
    /// its torso, so a mug has to paint over the apron.
    /// </summary>
    public static IReadOnlyList<AvatarSlot> Slots { get; } =
    [
        new(BackdropSlot, "Backdrop", AvatarSlotKind.Colour, "roast",
        [
            new("roast", "Roast", 0),
            new("espresso", "Espresso", 1),
            new("teal", "Teal", 2),
            new("copper", "Copper", 3),
            new("clay", "Clay", 4),
            new("moss", "Moss", 5),
            new("night", "Night", 6),
            new("gold", "Gold", 7)
        ]),
        new(ChassisSlot, "Chassis", AvatarSlotKind.Sprite, "cream",
        [
            new("cream", "Cream", 0),
            new("teal", "Teal", 1),
            new("copper", "Copper", 2),
            new("clay", "Clay", 3),
            new("moss", "Moss", 4),
            new("graphite", "Graphite", 5)
        ]),
        new(WearSlot, "Uniform", AvatarSlotKind.Sprite, NoneItemId,
        [
            new(NoneItemId, "None", -1),
            new("apron", "Apron", 0),
            new("hivis", "Hi-vis", 1),
            new("scarf", "Scarf", 2),
            new("bowtie", "Bow tie", 3),
            new("labcoat", "Lab coat", 4),
            new("bandolier", "Bandolier", 5)
        ]),
        new(VisorSlot, "Visor", AvatarSlotKind.Sprite, "dot",
        [
            new("dot", "Dot", 0),
            new("happy", "Happy", 1),
            new("sleepy", "Sleepy", 2),
            new("shades", "Shades", 3),
            new("scanline", "Scanline", 4),
            new("glitch", "Glitch", 5)
        ]),
        new(HatSlot, "Headgear", AvatarSlotKind.Sprite, NoneItemId,
        [
            new(NoneItemId, "None", -1),
            new("beanie", "Beanie", 0),
            new("barista", "Barista cap", 1),
            new("hardhat", "Hard hat", 2),
            new("headphones", "Headphones", 3),
            new("crown", "Crown", 4),
            new("bulb", "Antenna bulb", 5),
            new("toque", "Toque", 6)
        ]),
        new(HoldSlot, "Holding", AvatarSlotKind.Sprite, NoneItemId,
        [
            new(NoneItemId, "None", -1),
            new("mug", "Mug", 0),
            new("beanbag", "Bean bag", 1),
            new("key", "Key", 2),
            new("wrench", "Wrench", 3),
            new("terminal", "Terminal", 4),
            new("coin", "CAFE coin", 5)
        ])
    ];

    private static readonly FrozenDictionary<string, AvatarSlot> SlotsByKey =
        Slots.ToFrozenDictionary(slot => slot.Key, StringComparer.Ordinal);

    public static AvatarSlot GetSlot(string slotKey) =>
        SlotsByKey.TryGetValue(slotKey, out var slot)
            ? slot
            : throw new ArgumentOutOfRangeException(nameof(slotKey), slotKey, "Unknown avatar slot.");

    public static bool IsKnown(string slotKey, string? itemId) =>
        SlotsByKey.TryGetValue(slotKey, out var slot) && slot.Contains(itemId);

    public static RobotAvatar CreateDefault()
    {
        var avatar = new RobotAvatar();
        foreach (var slot in Slots)
        {
            avatar[slot.Key] = slot.DefaultItemId;
        }

        return avatar;
    }

    /// <summary>
    /// Replaces every unknown or blank item id with its slot default.
    /// </summary>
    /// <remarks>
    /// Always returns a new instance. Normalising in place would mutate the
    /// tracked entity during a <em>read</em>, so a later unrelated
    /// <c>SaveChanges</c> would quietly rewrite the row — and would do it
    /// with the fallback values, permanently destroying the ids of items the
    /// user picked before a rename.
    /// </remarks>
    public static RobotAvatar Normalize(RobotAvatar? avatar)
    {
        var normalized = new RobotAvatar();
        foreach (var slot in Slots)
        {
            var stored = avatar is null ? null : avatar[slot.Key];
            normalized[slot.Key] = slot.Resolve(string.IsNullOrWhiteSpace(stored) ? null : stored).Id;
        }

        return normalized;
    }
}
