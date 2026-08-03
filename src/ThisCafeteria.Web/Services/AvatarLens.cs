using System.Collections.Frozen;
using System.Globalization;
using ThisCafeteria.Domain.Avatars;

namespace ThisCafeteria.Web.Services;

/// <summary>
/// The window onto the robot that one slot's swatch cards look through.
/// </summary>
/// <remarks>
/// <para>
/// Coordinates are in frame units — the same 64px square every sprite layer is
/// drawn on, so <c>(0, 0, 64, 64)</c> is the whole robot and anything smaller
/// is a crop of it.
/// </para>
/// <para>
/// Two rules shape every window, and both are why the numbers look arbitrary.
/// It contains the union of its slot's frames with a little air, so no item is
/// ever cut off — the headgear window has to be wide enough for the hard hat's
/// brim and the headphone cups even though most hats are narrower. And its
/// bottom edge sits just under that union, so the part stands on the card's
/// bottom edge rather than floating in the middle of it.
/// </para>
/// <para>
/// The unions were measured off the shipped sheets, not read out of
/// <c>tools/generate_avatar_parts.py</c>; moving a landmark there moves them.
/// <c>AvatarLensTests</c> decodes the sheets and fails if a window has stopped
/// containing its slot's artwork.
/// </para>
/// </remarks>
/// <param name="CardSpan">
/// How much horizontal room this framing wants, relative to a square card.
/// Purely a layout knob: a window twice as wide as it is tall makes a card half
/// as tall, and four of those across leaves the grid mostly empty, so wide
/// windows ask for wider (and therefore fewer) columns.
/// </param>
public readonly record struct AvatarLens(int X, int Y, int Width, int Height, double CardSpan)
{
    /// <summary>The whole frame — what a slot with nothing declared falls back to.</summary>
    public static AvatarLens Full { get; } =
        new(0, 0, AvatarCatalog.FrameSize, AvatarCatalog.FrameSize, 1d);

    /// <summary>
    /// The custom properties a swatch card's geometry is built from.
    /// </summary>
    /// <remarks>
    /// Every value is a ratio so the CSS never has to divide: the card is
    /// <c>aspect-ratio: w / h</c>, the robot inside it is <c>100% * scale</c>
    /// wide, and the two insets slide it until the window lands on the card.
    /// The divisors differ because a percentage <c>left</c> resolves against
    /// the card's width while a percentage <c>bottom</c> resolves against its
    /// height — using one for both is the mistake this method exists to
    /// prevent, and it fails as a slightly-off crop rather than as an error.
    /// </remarks>
    public string Style()
    {
        var scale = AvatarCatalog.FrameSize / (double)Width;
        var left = -X / (double)Width;
        var bottom = (Y + Height - AvatarCatalog.FrameSize) / (double)Height;

        // InvariantCulture for the same reason AvatarSheetStyle needs it: a
        // comma decimal separator voids the declaration and the browser keeps
        // the previous slot's framing without complaint.
        return string.Create(
            CultureInfo.InvariantCulture,
            $"--lens-w:{Width};--lens-h:{Height};--lens-span:{CardSpan:0.###};--lens-scale:{scale:0.####};--lens-left:{left:0.####};--lens-bottom:{bottom:0.####}");
    }
}

/// <summary>
/// Where the swatch camera points, per slot.
/// </summary>
public static class AvatarLenses
{
    private static readonly FrozenDictionary<string, AvatarLens> ByKey =
        new Dictionary<string, AvatarLens>(StringComparer.Ordinal)
        {
            // The backdrop is the field the robot stands on, so its window is
            // the field: the card fills edge to edge with the colour.
            [AvatarCatalog.BackdropSlot] = new(0, 0, 64, 64, 1d),

            // The chassis is the whole robot. Sprite art spans x 11..54,
            // y 2..60; the extra margin keeps the antennae off the top edge.
            [AvatarCatalog.ChassisSlot] = new(2, 1, 62, 62, 1d),

            // Uniforms sit on the torso (x 21..45, y 31..50). Cut just below
            // the head so the garment is not competing with the face, and wide
            // enough to keep both mittens in shot.
            [AvatarCatalog.WearSlot] = new(15, 29, 35, 25, 1d),

            // The visor is only the screen (x 25..46, y 14..27), so the window
            // is the head: an expression cropped to its own bounding box reads
            // as a floating rectangle rather than as a face.
            [AvatarCatalog.VisorSlot] = new(20, 5, 32, 26, 1d),

            // Headgear is the awkward one — 52 wide and 25 tall at its worst
            // (the headphones), pinned to the top of the frame. The window has
            // to be nearly twice as wide as it is tall to hold it, which is
            // what CardSpan is compensating for.
            [AvatarCatalog.HatSlot] = new(3, 0, 58, 30, 1.25d),

            // Held props hang off the right mitten (x 44..59, y 31..51). The
            // strip of torso on the left edge is deliberate: without it the
            // prop reads as an inventory icon instead of something carried.
            // Held two columns off the frame's right edge rather than flush to
            // it — the sprites stop at 59, and the dead margin past them costs
            // more than the torso it buys back.
            [AvatarCatalog.HoldSlot] = new(38, 30, 24, 24, 1d)
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The window for a slot, or the whole frame if none is declared.
    /// </summary>
    /// <remarks>
    /// Falls back rather than throwing so that adding a slot to the catalog
    /// cannot take the profile page down — an unframed slot renders the way
    /// every slot did before this file existed. <c>AvatarLensTests</c> is what
    /// makes sure nobody ships on the fallback.
    /// </remarks>
    public static AvatarLens For(string slotKey) =>
        ByKey.TryGetValue(slotKey, out var lens) ? lens : AvatarLens.Full;

    /// <summary>Whether a slot has a window of its own. For tests.</summary>
    public static bool IsFramed(string slotKey) => ByKey.ContainsKey(slotKey);
}
