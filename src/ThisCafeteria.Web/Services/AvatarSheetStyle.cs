using System.Globalization;
using ThisCafeteria.Domain.Avatars;

namespace ThisCafeteria.Web.Services;

/// <summary>
/// Turns a catalog item into the CSS that shows its column of a sprite sheet.
/// </summary>
/// <remarks>
/// Lives outside the component so it can be tested directly. The arithmetic is
/// the one part of the avatar renderer that fails silently: a wrong divisor
/// still produces a perfectly valid background-position, it just lands on the
/// neighbouring hat, and nothing anywhere throws.
/// </remarks>
public static class AvatarSheetStyle
{
    /// <summary>Where the slot's sheets live, relative to wwwroot.</summary>
    public static string SheetUrl(AvatarSlot slot) => $"images/avatar/avatar-{slot.Key}.png";

    /// <summary>
    /// The inline style for one layer.
    /// </summary>
    /// <remarks>
    /// A sheet of N frames is stretched to N×100% of the layer box, so exactly
    /// one frame is visible. Percentage background-position is measured against
    /// the *overflow*, not the image, so the travel from the first column to
    /// the last is divided into (N-1) steps rather than N — that off-by-one is
    /// what puts everyone in the wrong hat.
    /// </remarks>
    public static string Layer(AvatarSlot slot, AvatarItem item)
    {
        if (item.IsEmpty)
        {
            return string.Empty;
        }

        var frames = slot.SheetFrames;
        var position = frames <= 1 ? 0d : item.SheetIndex * 100d / (frames - 1);

        // Two things this line is careful about.
        //
        // InvariantCulture: a locale with a comma decimal separator would emit
        // "background-position:16,6667% 0", the browser would drop the whole
        // declaration, and every robot would silently show column zero.
        //
        // Unquoted url(): Razor HTML-encodes a style attribute, so url('…')
        // reaches the page as url(&#x27;…&#x27;). Browsers decode that before
        // parsing the CSS so it does work — but it makes the markup unreadable
        // and unassertable, and bare paths are valid CSS here anyway.
        return string.Create(
            CultureInfo.InvariantCulture,
            $"background-image:url({SheetUrl(slot)});background-size:{frames * 100}% 100%;background-position:{position:0.####}% 0;");
    }
}
