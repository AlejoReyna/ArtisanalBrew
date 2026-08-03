using System.Globalization;
using FluentAssertions;
using ThisCafeteria.Domain.Avatars;
using ThisCafeteria.Web.Services;

namespace ThisCafeteria.UnitTests;

/// <summary>
/// The sprite-window arithmetic. Every failure mode here is silent — the CSS
/// stays valid and the robot simply wears the wrong thing — so it is pinned
/// rather than eyeballed.
/// </summary>
public sealed class AvatarSheetStyleTests
{
    private static AvatarSlot Hats => AvatarCatalog.GetSlot(AvatarCatalog.HatSlot);

    [Fact]
    public void TheFirstColumnSitsAtZeroAndTheLastAtOneHundredPercent()
    {
        var first = Hats.Items.Single(item => item.SheetIndex == 0);
        var last = Hats.Items.Single(item => item.SheetIndex == Hats.SheetFrames - 1);

        AvatarSheetStyle.Layer(Hats, first).Should().Contain("background-position:0% 0");
        AvatarSheetStyle.Layer(Hats, last).Should().Contain("background-position:100% 0");
    }

    [Fact]
    public void TheSheetIsStretchedToOneBoxPerFrame()
    {
        // 7 hats over a 64px box means 700% wide, so exactly one frame shows.
        AvatarSheetStyle.Layer(Hats, Hats.Items[1]).Should().Contain("background-size:700% 100%");
    }

    [Fact]
    public void ColumnsAreSpacedOverFramesMinusOne()
    {
        // The classic off-by-one: dividing by 7 instead of 6 puts column 3 at
        // 42.857% instead of 50% — still valid CSS, still the wrong hat.
        var third = Hats.Items.Single(item => item.SheetIndex == 3);

        AvatarSheetStyle.Layer(Hats, third).Should().Contain("background-position:50% 0");
    }

    [Fact]
    public void EveryCatalogItemLandsOnAWholeColumn()
    {
        foreach (var slot in AvatarCatalog.Slots.Where(s => s.Kind == AvatarSlotKind.Sprite))
        {
            foreach (var item in slot.Items.Where(i => !i.IsEmpty))
            {
                var style = AvatarSheetStyle.Layer(slot, item);

                // Recover the position CSS actually got and turn it back into a
                // column index; it has to be the one the catalog declared.
                var percent = double.Parse(
                    style.Split("background-position:")[1].Split('%')[0],
                    CultureInfo.InvariantCulture);
                var column = percent / 100d * (slot.SheetFrames - 1);

                column.Should().BeApproximately(item.SheetIndex, 0.001,
                    "'{0}' is column {1} of slot '{2}'", item.Id, item.SheetIndex, slot.Key);
            }
        }
    }

    [Fact]
    public void AnEmptyItemDrawsNoLayerAtAll()
    {
        var none = Hats.Items.Single(item => item.Id == AvatarCatalog.NoneItemId);

        AvatarSheetStyle.Layer(Hats, none).Should().BeEmpty();
    }

    [Fact]
    public void DecimalsAreWrittenWithAPointOnAnyMachine()
    {
        // A comma separator voids the whole declaration and every robot
        // silently falls back to column zero.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("es-MX");
            var chassis = AvatarCatalog.GetSlot(AvatarCatalog.ChassisSlot);

            var style = AvatarSheetStyle.Layer(chassis, chassis.Items[1]);

            style.Should().Contain("background-position:20% 0").And.NotContain(",");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void EverySlotPointsAtTheSheetTheGeneratorWrites()
    {
        foreach (var slot in AvatarCatalog.Slots.Where(s => s.Kind == AvatarSlotKind.Sprite))
        {
            AvatarSheetStyle.SheetUrl(slot).Should().Be($"images/avatar/avatar-{slot.Key}.png");
        }
    }
}
