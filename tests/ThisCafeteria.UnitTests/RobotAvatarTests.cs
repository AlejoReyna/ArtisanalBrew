using FluentAssertions;
using ThisCafeteria.Domain.Avatars;

namespace ThisCafeteria.UnitTests;

public sealed class RobotAvatarTests
{
    private const string ChecksumAddress = "0x9D5305A9621AAFb5b5F8ba7a9977e3d96ea7eceB";
    private const string LowercaseAddress = "0x9d5305a9621aafb5b5f8ba7a9977e3d96ea7eceb";
    private const string OtherAddress = "0x1f9840a85d5aF5bf1D1762F925BDADdC4201F984";
    private const string SolanaAddress = "7EqQdEULxWcraVx3mXKFjc84LhCkMGZCkRuDpvcMwJeK";

    // ── Catalog integrity ───────────────────────────────────────────────────
    // The sprite generator writes sheet columns from these indices and the
    // renderer reads them back, so a typo here is a silently misaligned hat.

    [Fact]
    public void Catalog_ShouldGiveEverySlotUniqueItemIds()
    {
        foreach (var slot in AvatarCatalog.Slots)
        {
            slot.Items.Select(item => item.Id).Should().OnlyHaveUniqueItems(
                "slot '{0}' must not repeat an item id", slot.Key);
        }
    }

    [Fact]
    public void Catalog_ShouldNumberSpriteColumnsContiguouslyFromZero()
    {
        foreach (var slot in AvatarCatalog.Slots)
        {
            var columns = slot.Items
                .Where(item => !item.IsEmpty)
                .Select(item => item.SheetIndex)
                .OrderBy(index => index);

            columns.Should().Equal(
                Enumerable.Range(0, slot.SheetFrames),
                "slot '{0}' indexes a sheet by background-position, so a gap or a " +
                "duplicate column would render the wrong part", slot.Key);
        }
    }

    [Fact]
    public void Catalog_ShouldResolveEveryDeclaredDefault()
    {
        foreach (var slot in AvatarCatalog.Slots)
        {
            slot.Contains(slot.DefaultItemId).Should().BeTrue(
                "slot '{0}' names '{1}' as its default", slot.Key, slot.DefaultItemId);
        }
    }

    [Fact]
    public void Catalog_ShouldGiveEachSlotAtMostOneEmptyItem()
    {
        foreach (var slot in AvatarCatalog.Slots)
        {
            slot.Items.Count(item => item.IsEmpty).Should().BeLessThanOrEqualTo(1,
                "slot '{0}' can only have one way to wear nothing", slot.Key);
        }
    }

    // ── Normalize: the read-path guard ──────────────────────────────────────

    [Fact]
    public void Normalize_ShouldFallBackToDefaultsWhenTheAvatarIsNull()
    {
        var normalized = AvatarCatalog.Normalize(null);

        normalized.HasSameLook(AvatarCatalog.CreateDefault()).Should().BeTrue();
    }

    [Fact]
    public void Normalize_ShouldReplaceItemIdsThatAreNoLongerInTheCatalog()
    {
        var stored = new RobotAvatar
        {
            Chassis = "chassis-that-was-renamed",
            Visor = "happy",
            Hat = "crown",
            Wear = "",
            Hold = "   ",
            Backdrop = "night"
        };

        var normalized = AvatarCatalog.Normalize(stored);

        normalized.Chassis.Should().Be(AvatarCatalog.GetSlot(AvatarCatalog.ChassisSlot).DefaultItemId);
        normalized.Wear.Should().Be(AvatarCatalog.NoneItemId);
        normalized.Hold.Should().Be(AvatarCatalog.NoneItemId);

        // Everything still in the catalog survives untouched.
        normalized.Visor.Should().Be("happy");
        normalized.Hat.Should().Be("crown");
        normalized.Backdrop.Should().Be("night");
    }

    [Fact]
    public void Normalize_ShouldNotMutateTheStoredAvatar()
    {
        // The stored instance is an EF-tracked owned entity. Normalising it in
        // place during a read would mark the row dirty, so the next unrelated
        // SaveChanges would overwrite the user's real pick with the fallback.
        var stored = new RobotAvatar { Chassis = "gone", Visor = "gone" };

        var normalized = AvatarCatalog.Normalize(stored);

        stored.Chassis.Should().Be("gone");
        stored.Visor.Should().Be("gone");
        normalized.Should().NotBeSameAs(stored);
    }

    // ── Seed: the same wallet must always get the same robot ────────────────

    [Fact]
    public void FromWallet_ShouldBeDeterministic()
    {
        AvatarSeed.FromWallet(ChecksumAddress)
            .HasSameLook(AvatarSeed.FromWallet(ChecksumAddress))
            .Should().BeTrue();
    }

    [Fact]
    public void FromWallet_ShouldIgnoreEvmChecksumCasing()
    {
        // The same wallet reaches us checksummed from one path and lowercase
        // from another; both have to render the same robot.
        AvatarSeed.FromWallet(ChecksumAddress)
            .HasSameLook(AvatarSeed.FromWallet(LowercaseAddress))
            .Should().BeTrue();
    }

    [Fact]
    public void FromWallet_ShouldTreatBase58CasingAsSignificant()
    {
        // Base58 is case-sensitive, so lower-casing a Solana address would
        // collapse distinct wallets onto one avatar.
        AvatarSeed.FromWallet(SolanaAddress)
            .HasSameLook(AvatarSeed.FromWallet(SolanaAddress.ToLowerInvariant()))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromWallet_ShouldFallBackToDefaultsWithoutAWallet(string? address)
    {
        AvatarSeed.FromWallet(address)
            .HasSameLook(AvatarCatalog.CreateDefault())
            .Should().BeTrue();
    }

    [Fact]
    public void FromWallet_ShouldGiveDifferentWalletsDifferentRobots()
    {
        AvatarSeed.FromWallet(ChecksumAddress)
            .HasSameLook(AvatarSeed.FromWallet(OtherAddress))
            .Should().BeFalse();
    }

    [Fact]
    public void FromWallet_ShouldOnlyEverPickCatalogItems()
    {
        foreach (var address in new[] { ChecksumAddress, OtherAddress, SolanaAddress })
        {
            var seeded = AvatarSeed.FromWallet(address);
            foreach (var slot in AvatarCatalog.Slots)
            {
                AvatarCatalog.IsKnown(slot.Key, seeded[slot.Key]).Should().BeTrue(
                    "the seed for {0} put '{1}' in slot '{2}'", address, seeded[slot.Key], slot.Key);
            }
        }
    }

    [Fact]
    public void FromWallet_ShouldKeepItsHashStableAcrossReleases()
    {
        // Pinned on purpose. An unedited profile renders from the seed every
        // time it loads, so changing the hash silently restyles every account
        // that never opened the editor. If this fails, the hash changed — that
        // is a migration, not a refactor.
        var seeded = AvatarSeed.FromWallet(ChecksumAddress);

        var look = string.Join(
            " ",
            AvatarCatalog.Slots.Select(slot => $"{slot.Key}={seeded[slot.Key]}"));

        look.Should().Be("backdrop=night chassis=copper wear=hivis visor=glitch hat=beanie hold=beanbag");
    }

    [Fact]
    public void IsUnchangedSeed_ShouldSeparateAChosenLookFromAGeneratedOne()
    {
        var seeded = AvatarSeed.FromWallet(ChecksumAddress);
        AvatarSeed.IsUnchangedSeed(ChecksumAddress, seeded).Should().BeTrue();

        var chosen = seeded.Clone();
        chosen.Hat = chosen.Hat == "crown" ? "toque" : "crown";
        AvatarSeed.IsUnchangedSeed(ChecksumAddress, chosen).Should().BeFalse();
    }

    [Fact]
    public void Indexer_ShouldRejectAnUnknownSlotKey()
    {
        var avatar = AvatarCatalog.CreateDefault();

        var read = () => avatar["shoes"];

        read.Should().Throw<ArgumentOutOfRangeException>();
    }
}
