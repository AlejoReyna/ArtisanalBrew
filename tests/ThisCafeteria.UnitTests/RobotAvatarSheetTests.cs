using System.Buffers.Binary;
using FluentAssertions;
using ThisCafeteria.Domain.Avatars;

namespace ThisCafeteria.UnitTests;

/// <summary>
/// Ties the shipped sprite sheets to <see cref="AvatarCatalog"/>.
/// </summary>
/// <remarks>
/// The catalog and <c>tools/generate_avatar_parts.py</c> are two hands writing
/// the same contract: the catalog says a slot has N sprite columns and the
/// renderer indexes them with <c>background-position</c>, so a sheet that is
/// one column short silently shows the wrong hat on every avatar past the gap.
/// Nothing at build time couples them, so this is the coupling.
///
/// If one of these fails, the fix is almost always to run the generator:
/// <c>python3 tools/generate_avatar_parts.py</c>
/// </remarks>
public sealed class RobotAvatarSheetTests
{
    private static readonly DirectoryInfo AvatarImages = LocateAvatarImages();

    [Fact]
    public void EverySpriteSlotShipsASheetWithExactlyItsCatalogColumns()
    {
        foreach (var slot in AvatarCatalog.Slots.Where(s => s.Kind == AvatarSlotKind.Sprite))
        {
            var file = new FileInfo(Path.Combine(AvatarImages.FullName, $"avatar-{slot.Key}.png"));
            file.Exists.Should().BeTrue("slot '{0}' is a sprite slot, so {1} must exist", slot.Key, file.Name);

            var (width, height) = ReadPngSize(file);

            height.Should().Be(AvatarCatalog.FrameSize,
                "{0} must be one row of {1}px frames", file.Name, AvatarCatalog.FrameSize);
            width.Should().Be(slot.SheetFrames * AvatarCatalog.FrameSize,
                "the catalog gives slot '{0}' {1} sprite columns, so {2} must be {1}x{3}px wide — " +
                "regenerate it with tools/generate_avatar_parts.py",
                slot.Key, slot.SheetFrames, file.Name, AvatarCatalog.FrameSize);
        }
    }

    [Fact]
    public void ColourSlotsShipNoSheet()
    {
        // The backdrop is CSS colours keyed by item id. A PNG appearing here
        // means someone drew sprites the renderer will never load.
        foreach (var slot in AvatarCatalog.Slots.Where(s => s.Kind == AvatarSlotKind.Colour))
        {
            File.Exists(Path.Combine(AvatarImages.FullName, $"avatar-{slot.Key}.png"))
                .Should().BeFalse("slot '{0}' renders as CSS colour, not as a sheet", slot.Key);
        }
    }

    [Fact]
    public void NoSheetIsShippedForASlotTheCatalogDroppped()
    {
        var expected = AvatarCatalog.Slots
            .Where(slot => slot.Kind == AvatarSlotKind.Sprite)
            .Select(slot => $"avatar-{slot.Key}.png")
            .ToHashSet(StringComparer.Ordinal);

        AvatarImages.GetFiles("avatar-*.png")
            .Select(file => file.Name)
            .Should().BeSubsetOf(expected, "a sheet with no slot behind it is dead weight in wwwroot");
    }

    private static (int Width, int Height) ReadPngSize(FileInfo file)
    {
        // IHDR is fixed at the head of every PNG: 8-byte signature, 4-byte
        // chunk length, 4-byte "IHDR", then width and height as big-endian
        // uint32. Cheaper and dependency-free next to decoding the image.
        Span<byte> header = stackalloc byte[24];
        using var stream = file.OpenRead();
        stream.ReadExactly(header);

        return (
            BinaryPrimitives.ReadInt32BigEndian(header[16..20]),
            BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
    }

    private static DirectoryInfo LocateAvatarImages()
    {
        // Walk out of bin/<config>/<tfm> to the repo root rather than counting
        // "../" five times, which breaks the moment a TFM or configuration
        // changes the depth.
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
