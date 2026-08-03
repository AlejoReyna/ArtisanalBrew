using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using FluentAssertions;
using ThisCafeteria.Domain.Avatars;
using ThisCafeteria.Web.Services;

namespace ThisCafeteria.UnitTests;

/// <summary>
/// Ties the swatch camera to the sprites it is pointed at.
/// </summary>
/// <remarks>
/// <see cref="AvatarLenses"/> holds hand-measured rectangles, and nothing at
/// build time stops <c>tools/generate_avatar_parts.py</c> from moving the art
/// out from under them. The failure is quiet — the card still renders, the crop
/// is simply wrong, and a hat brim or a mug handle is missing off the edge — so
/// these tests decode the shipped sheets and check the windows still hold.
///
/// If one fails, either re-measure the slot's window or put the sprite back.
/// </remarks>
public sealed class AvatarLensTests
{
    private static readonly DirectoryInfo AvatarImages = LocateAvatarImages();

    [Fact]
    public void EverySlotIsFramed()
    {
        // The fallback is the whole frame, which renders but throws away the
        // entire point of the feature, so it must never be what ships.
        foreach (var slot in AvatarCatalog.Slots)
        {
            AvatarLenses.IsFramed(slot.Key).Should().BeTrue(
                "slot '{0}' is in the catalog, so AvatarLenses must say where its cards point", slot.Key);
        }
    }

    [Fact]
    public void NoWindowLeavesTheFrame()
    {
        foreach (var slot in AvatarCatalog.Slots)
        {
            var lens = AvatarLenses.For(slot.Key);

            lens.Width.Should().BePositive();
            lens.Height.Should().BePositive();
            lens.X.Should().BeGreaterThanOrEqualTo(0);
            lens.Y.Should().BeGreaterThanOrEqualTo(0);

            // A window hanging off the sprite shows dead space the disc colour
            // cannot reach, and the card gets a bar of raw card background.
            (lens.X + lens.Width).Should().BeLessThanOrEqualTo(AvatarCatalog.FrameSize,
                "slot '{0}' must stay inside the {1}px frame", slot.Key, AvatarCatalog.FrameSize);
            (lens.Y + lens.Height).Should().BeLessThanOrEqualTo(AvatarCatalog.FrameSize,
                "slot '{0}' must stay inside the {1}px frame", slot.Key, AvatarCatalog.FrameSize);
        }
    }

    [Fact]
    public void EveryItemInASlotIsFullyInsideThatSlotsWindow()
    {
        foreach (var slot in SpriteSlots())
        {
            var lens = AvatarLenses.For(slot.Key);
            var sheet = Sheet(slot);

            foreach (var item in slot.Items.Where(i => !i.IsEmpty))
            {
                var art = sheet.Bounds(item.SheetIndex);
                art.Should().NotBeNull("'{0}' draws nothing at column {1}", item.Id, item.SheetIndex);

                var (left, top, right, bottom) = art!.Value;

                left.Should().BeGreaterThanOrEqualTo(lens.X,
                    "'{0}' starts at x {1} and slot '{2}' frames from x {3}", item.Id, left, slot.Key, lens.X);
                right.Should().BeLessThan(lens.X + lens.Width,
                    "'{0}' reaches x {1} and slot '{2}' frames to x {3}", item.Id, right, slot.Key, lens.X + lens.Width - 1);
                top.Should().BeGreaterThanOrEqualTo(lens.Y,
                    "'{0}' starts at y {1} and slot '{2}' frames from y {3}", item.Id, top, slot.Key, lens.Y);
                bottom.Should().BeLessThan(lens.Y + lens.Height,
                    "'{0}' reaches y {1} and slot '{2}' frames to y {3}", item.Id, bottom, slot.Key, lens.Y + lens.Height - 1);
            }
        }
    }

    [Fact]
    public void EverySlotStandsOnTheBottomOfItsCard()
    {
        // The whole layout rests on this: the window is bottomed just under the
        // part, so the card's bottom edge is the part's ground line. Let it
        // drift and the sprite floats in the middle of the card with a band of
        // backdrop under it, which is the look this replaced.
        foreach (var slot in SpriteSlots())
        {
            var lens = AvatarLenses.For(slot.Key);
            var sheet = Sheet(slot);

            var lowest = slot.Items
                .Where(item => !item.IsEmpty)
                .Select(item => sheet.Bounds(item.SheetIndex)!.Value.Bottom)
                .Max();

            var floor = lens.Y + lens.Height - 1;

            (floor - lowest).Should().BeLessThanOrEqualTo(lens.Height / 5,
                "slot '{0}' bottoms out at y {1} but its window runs to y {2} — the part has to land in the " +
                "bottom fifth of the card, not float above it", slot.Key, lowest, floor);
        }
    }

    [Fact]
    public void TheWindowIsScaledAndSlidUntilItFillsTheCard()
    {
        // Headgear: x 3, y 0, 58 wide, 30 tall.
        //   scale  64/58        = 1.1034 — the robot is wider than the card
        //   left   -3/58        = -0.0517 of the card's width, so frame x=3
        //                         lands on the card's left edge
        //   bottom (0+30-64)/30 = -1.1333 of the card's *height*, dropping the
        //                         34 unseen rows below the card's bottom edge
        var style = AvatarLenses.For(AvatarCatalog.HatSlot).Style();

        style.Should().Contain("--lens-w:58").And.Contain("--lens-h:30");
        style.Should().Contain("--lens-scale:1.1034");
        style.Should().Contain("--lens-left:-0.0517");
        style.Should().Contain("--lens-bottom:-1.1333");
    }

    [Fact]
    public void AFullFrameWindowNeitherScalesNorSlides()
    {
        AvatarLenses.For(AvatarCatalog.BackdropSlot).Style()
            .Should().Contain("--lens-scale:1")
            .And.Contain("--lens-left:0")
            .And.Contain("--lens-bottom:0");
    }

    [Fact]
    public void DecimalsAreWrittenWithAPointOnAnyMachine()
    {
        // Same trap as AvatarSheetStyle: a comma separator voids the
        // declaration and the card silently keeps the previous tab's framing.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("es-MX");

            AvatarLenses.For(AvatarCatalog.HatSlot).Style().Should().NotContain(",");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    private static IEnumerable<AvatarSlot> SpriteSlots() =>
        AvatarCatalog.Slots.Where(slot => slot.Kind == AvatarSlotKind.Sprite);

    private static SpriteSheet Sheet(AvatarSlot slot) =>
        SpriteSheet.Load(Path.Combine(AvatarImages.FullName, $"avatar-{slot.Key}.png"));

    /// <summary>
    /// Just enough PNG to ask where the ink is.
    /// </summary>
    /// <remarks>
    /// The alternative was adding an imaging package to the test project for
    /// one alpha lookup. The generator writes 8-bit RGBA, non-interlaced, so
    /// that is all this reads — anything else fails loudly rather than
    /// guessing.
    /// </remarks>
    private sealed class SpriteSheet
    {
        private readonly byte[] _pixels;
        private readonly int _stride;

        private SpriteSheet(byte[] pixels, int width)
        {
            _pixels = pixels;
            _stride = width * 4;
        }

        /// <summary>The tightest box around the opaque pixels of one frame, in
        /// frame coordinates, or null if the frame is empty.</summary>
        public (int Left, int Top, int Right, int Bottom)? Bounds(int frame)
        {
            var origin = frame * AvatarCatalog.FrameSize;
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;

            for (var y = 0; y < AvatarCatalog.FrameSize; y++)
            {
                for (var x = 0; x < AvatarCatalog.FrameSize; x++)
                {
                    if (_pixels[y * _stride + (origin + x) * 4 + 3] == 0)
                    {
                        continue;
                    }

                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }

            return right < 0 ? null : (left, top, right, bottom);
        }

        public static SpriteSheet Load(string path)
        {
            var bytes = File.ReadAllBytes(path);

            var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
            bytes[24].Should().Be(8, "{0} must be 8 bits per channel", path);
            bytes[25].Should().Be(6, "{0} must be RGBA", path);
            bytes[28].Should().Be(0, "{0} must not be interlaced", path);

            return new SpriteSheet(Unfilter(Inflate(Idat(bytes)), width, height), width);
        }

        /// <summary>Every IDAT chunk's payload, in order — one zlib stream.</summary>
        private static byte[] Idat(byte[] png)
        {
            using var joined = new MemoryStream();
            var offset = 8;

            while (offset + 8 <= png.Length)
            {
                var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
                var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);

                if (type == "IDAT")
                {
                    joined.Write(png, offset + 8, length);
                }

                // 4 length + 4 type + payload + 4 CRC.
                offset += 12 + length;
            }

            return joined.ToArray();
        }

        private static byte[] Inflate(byte[] zlib)
        {
            using var source = new MemoryStream(zlib);
            using var decompressor = new ZLibStream(source, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            decompressor.CopyTo(raw);
            return raw.ToArray();
        }

        /// <summary>
        /// Reverses the per-scanline filters, in place, into a flat RGBA buffer.
        /// </summary>
        /// <remarks>
        /// Each scanline is one filter byte then <c>width * 4</c> bytes, and
        /// every predictor works on already-reconstructed neighbours: the pixel
        /// four bytes back on this line, the one directly above, and the one
        /// above-left. Filters are per-line, so a sheet mixes all five.
        /// </remarks>
        private static byte[] Unfilter(byte[] filtered, int width, int height)
        {
            const int bpp = 4;
            var stride = width * bpp;
            var output = new byte[stride * height];

            for (var y = 0; y < height; y++)
            {
                var filter = filtered[y * (stride + 1)];
                var line = y * (stride + 1) + 1;

                for (var i = 0; i < stride; i++)
                {
                    int a = i >= bpp ? output[y * stride + i - bpp] : 0;
                    int b = y > 0 ? output[(y - 1) * stride + i] : 0;
                    int c = i >= bpp && y > 0 ? output[(y - 1) * stride + i - bpp] : 0;

                    var predicted = filter switch
                    {
                        0 => 0,
                        1 => a,
                        2 => b,
                        3 => (a + b) / 2,
                        4 => Paeth(a, b, c),
                        _ => throw new InvalidDataException($"Unknown PNG filter {filter} on row {y}.")
                    };

                    output[y * stride + i] = (byte)(filtered[line + i] + predicted);
                }
            }

            return output;
        }

        private static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var pa = Math.Abs(p - a);
            var pb = Math.Abs(p - b);
            var pc = Math.Abs(p - c);

            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }

    private static DirectoryInfo LocateAvatarImages()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ThisCafeteria.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the tests must run from inside the repository");

        return new DirectoryInfo(Path.Combine(
            directory!.FullName, "src", "ThisCafeteria.Web", "wwwroot", "images", "avatar"));
    }
}
