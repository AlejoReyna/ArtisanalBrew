using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Domain.Avatars;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.UnitTests;

/// <summary>
/// The avatar is an optional owned entity mapped with <c>ToJson</c>, which is
/// the part of the schema most likely to surprise: EF has to round-trip the
/// whole look through one column and keep <c>null</c> distinguishable from a
/// default-looking robot, because <c>null</c> is what makes the profile page
/// fall back to the wallet seed.
/// </summary>
public sealed class UserProfileAvatarPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public UserProfileAvatarPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new AppDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task AProfileThatHasNeverBeenEditedKeepsANullAvatar()
    {
        var id = await SeedAsync(avatar: null);

        await using var context = new AppDbContext(_options);
        var stored = await context.UserProfiles.SingleAsync(profile => profile.Id == id);

        // Not "an avatar with default values" — actually absent. The read path
        // keys the wallet-seed fallback off exactly this.
        stored.Avatar.Should().BeNull();
    }

    [Fact]
    public async Task AnEditedAvatarSurvivesTheRoundTrip()
    {
        var chosen = new RobotAvatar
        {
            Backdrop = "night",
            Chassis = "copper",
            Wear = "apron",
            Visor = "shades",
            Hat = "toque",
            Hold = "mug"
        };

        var id = await SeedAsync(chosen);

        await using var context = new AppDbContext(_options);
        var stored = await context.UserProfiles.SingleAsync(profile => profile.Id == id);

        stored.Avatar.Should().NotBeNull();
        stored.Avatar!.HasSameLook(chosen).Should().BeTrue();
    }

    [Fact]
    public async Task SavingAnAvatarOntoAProfileThatHadNoneUpdatesTheColumn()
    {
        var id = await SeedAsync(avatar: null);

        await using (var editing = new AppDbContext(_options))
        {
            var profile = await editing.UserProfiles.SingleAsync(item => item.Id == id);
            profile.Avatar = AvatarSeed.FromWallet("0x9D5305A9621AAFb5b5F8ba7a9977e3d96ea7eceB");
            profile.Avatar.Hat = "crown";
            await editing.SaveChangesAsync();
        }

        await using var context = new AppDbContext(_options);
        var stored = await context.UserProfiles.SingleAsync(profile => profile.Id == id);

        stored.Avatar.Should().NotBeNull();
        stored.Avatar!.Hat.Should().Be("crown");
        stored.Avatar.Chassis.Should().Be("copper");
    }

    [Fact]
    public async Task ClearingAnAvatarReturnsTheProfileToTheSeededState()
    {
        var id = await SeedAsync(AvatarCatalog.CreateDefault());

        await using (var editing = new AppDbContext(_options))
        {
            var profile = await editing.UserProfiles.SingleAsync(item => item.Id == id);
            profile.Avatar = null;
            await editing.SaveChangesAsync();
        }

        await using var context = new AppDbContext(_options);
        var stored = await context.UserProfiles.SingleAsync(profile => profile.Id == id);

        stored.Avatar.Should().BeNull();
    }

    [Fact]
    public async Task AnIdThatLeftTheCatalogIsStoredVerbatimAndOnlyFixedOnRead()
    {
        // A row written before an item was renamed. The column keeps what was
        // written — normalising on write would destroy the evidence — and the
        // catalog repairs it on the way to the screen.
        var id = await SeedAsync(new RobotAvatar
        {
            Backdrop = "night",
            Chassis = "chassis-that-was-renamed",
            Wear = AvatarCatalog.NoneItemId,
            Visor = "shades",
            Hat = AvatarCatalog.NoneItemId,
            Hold = AvatarCatalog.NoneItemId
        });

        await using var context = new AppDbContext(_options);
        var stored = await context.UserProfiles.SingleAsync(profile => profile.Id == id);

        stored.Avatar!.Chassis.Should().Be("chassis-that-was-renamed");
        AvatarCatalog.Normalize(stored.Avatar).Chassis.Should().Be("cream");
        AvatarCatalog.Normalize(stored.Avatar).Visor.Should().Be("shades");
    }

    private async Task<Guid> SeedAsync(RobotAvatar? avatar)
    {
        await using var context = new AppDbContext(_options);
        var profile = new UserProfile
        {
            Email = $"{Guid.NewGuid():N}@wallet.thiscafeteria.local",
            DisplayName = "0x9D53...eceB",
            Avatar = avatar
        };

        context.UserProfiles.Add(profile);
        await context.SaveChangesAsync();
        return profile.Id;
    }
}
