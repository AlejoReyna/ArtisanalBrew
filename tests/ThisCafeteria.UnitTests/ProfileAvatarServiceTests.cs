using FluentAssertions;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Application.Validation;
using ThisCafeteria.Domain.Avatars;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Identity;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Infrastructure.Persistence.Repositories;

namespace ThisCafeteria.UnitTests;

/// <summary>
/// The avatar path from <see cref="ProfileService"/> down through the real
/// repository to a real database — the layer where "does saving an owned JSON
/// column actually persist" stops being a guess.
/// </summary>
public sealed class ProfileAvatarServiceTests : IDisposable
{
    private const string Wallet = "0x9D5305A9621AAFb5b5F8ba7a9977e3d96ea7eceB";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public ProfileAvatarServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var context = new AppDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task AFreshProfileRendersTheRobotDerivedFromItsWallet()
    {
        var profileId = await SeedAsync();
        var service = CreateService(out _);

        var dashboard = await service.GetProfileDashboardAsync(profileId);

        dashboard.Avatar.Should().Be(RobotAvatarDto.Resolve(null, Wallet));
        dashboard.Avatar.Chassis.Should().Be("copper", "that is what this wallet seeds to");
    }

    [Fact]
    public async Task SavingAnAvatarPersistsItAndOverridesTheSeed()
    {
        var profileId = await SeedAsync();
        var chosen = new UpdateAvatarRequest("night", "moss", "apron", "happy", "toque", "mug");

        var updated = await CreateService(out _).UpdateAvatarAsync(profileId, chosen);
        updated.Avatar.Chassis.Should().Be("moss");

        // A completely separate service over a separate context: proves the
        // look came back off the database, not out of the first instance.
        var reloaded = await CreateService(out _).GetProfileDashboardAsync(profileId);

        reloaded.Avatar.Should().Be(new RobotAvatarDto("night", "moss", "apron", "happy", "toque", "mug"));
    }

    [Fact]
    public async Task ResettingClearsTheColumnSoTheProfileTracksItsSeedAgain()
    {
        var profileId = await SeedAsync();
        await CreateService(out _).UpdateAvatarAsync(
            profileId,
            new UpdateAvatarRequest("gold", "clay", "hivis", "shades", "crown", "coin"));

        var reset = await CreateService(out _).ResetAvatarAsync(profileId);

        reset.Avatar.Should().Be(RobotAvatarDto.Resolve(null, Wallet));

        await using var context = new AppDbContext(_options);
        var stored = await context.UserProfiles.SingleAsync(profile => profile.Id == profileId);
        stored.Avatar.Should().BeNull("resetting must restore the never-edited state, not save the seed's values");
    }

    [Fact]
    public async Task AnItemOutsideTheCatalogIsRejectedBeforeItReachesTheDatabase()
    {
        var profileId = await SeedAsync();
        var service = CreateService(out _);

        var save = () => service.UpdateAvatarAsync(
            profileId,
            new UpdateAvatarRequest("night", "moss", "apron", "happy", "sombrero-de-charro", "mug"));

        (await save.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(error => error.PropertyName == "Hat");

        await using var context = new AppDbContext(_options);
        var stored = await context.UserProfiles.SingleAsync(profile => profile.Id == profileId);
        stored.Avatar.Should().BeNull("a rejected save must not partially write");
    }

    [Fact]
    public async Task AProfileWithNoWalletFallsBackToTheCatalogDefaults()
    {
        var profileId = await SeedAsync(wallet: null);

        var dashboard = await CreateService(out _).GetProfileDashboardAsync(profileId);

        dashboard.Avatar.Should().Be(RobotAvatarDto.Resolve(null, null));
        dashboard.Avatar.Chassis.Should().Be(AvatarCatalog.GetSlot(AvatarCatalog.ChassisSlot).DefaultItemId);
    }

    [Fact]
    public async Task AnIdRetiredAfterItWasSavedIsRepairedOnReadWithoutRewritingTheRow()
    {
        var profileId = await SeedAsync();

        // Write straight past the validator, the way a row saved before an
        // item was renamed looks today.
        await using (var seeding = new AppDbContext(_options))
        {
            var profile = await seeding.UserProfiles.SingleAsync(item => item.Id == profileId);
            profile.Avatar = new RobotAvatar
            {
                Backdrop = "night",
                Chassis = "chassis-that-was-renamed",
                Wear = "apron",
                Visor = "happy",
                Hat = AvatarCatalog.NoneItemId,
                Hold = AvatarCatalog.NoneItemId
            };
            await seeding.SaveChangesAsync();
        }

        var dashboard = await CreateService(out _).GetProfileDashboardAsync(profileId);
        dashboard.Avatar.Chassis.Should().Be("cream", "an unknown id falls back to the slot default");
        dashboard.Avatar.Wear.Should().Be("apron", "the rest of the look is untouched");

        await using var context = new AppDbContext(_options);
        var stored = await context.UserProfiles.SingleAsync(profile => profile.Id == profileId);
        stored.Avatar!.Chassis.Should().Be("chassis-that-was-renamed",
            "reading must not quietly overwrite what the user actually picked");
    }

    [Fact]
    public async Task TheHeaderLookupReturnsTheStoredRobotForALinkedAccount()
    {
        var profileId = await SeedAsync();
        var userId = await ApplicationUserIdAsync(profileId);
        await CreateService(out _).UpdateAvatarAsync(
            profileId,
            new UpdateAvatarRequest("gold", "clay", "hivis", "shades", "crown", "coin"));

        var avatar = await CreateService(out _).GetAvatarForApplicationUserAsync(userId);

        avatar.Should().Be(new RobotAvatarDto("gold", "clay", "hivis", "shades", "crown", "coin"));
    }

    [Fact]
    public async Task TheHeaderLookupNeverCreatesAProfile()
    {
        // A wallet that signed in but has not visited /profile yet has no
        // UserProfile row. Rendering the header must not conjure one — that is
        // the whole reason this path exists instead of EnsureProfileLinkedAsync.
        var userId = await SeedUnlinkedUserAsync();

        int Before;
        await using (var context = new AppDbContext(_options))
        {
            Before = await context.UserProfiles.CountAsync();
        }

        var avatar = await CreateService(out _).GetAvatarForApplicationUserAsync(userId);

        await using var after = new AppDbContext(_options);
        (await after.UserProfiles.CountAsync()).Should().Be(Before, "the header is a read");
        (await after.Carts.CountAsync()).Should().Be(0);

        // It still gets a robot: the wallet is all the seed ever needed.
        avatar.Should().Be(RobotAvatarDto.Resolve(null, Wallet));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a-user-that-does-not-exist")]
    public async Task TheHeaderLookupFallsBackToDefaultsRatherThanThrowing(string userId)
    {
        var avatar = await CreateService(out _).GetAvatarForApplicationUserAsync(userId);

        avatar.Should().Be(RobotAvatarDto.Resolve(null, null));
    }

    private async Task<string> ApplicationUserIdAsync(Guid profileId)
    {
        await using var context = new AppDbContext(_options);
        return (await context.Users.SingleAsync(user => user.UserProfileId == profileId)).Id;
    }

    private async Task<string> SeedUnlinkedUserAsync()
    {
        await using var context = new AppDbContext(_options);
        var user = new ApplicationUser
        {
            UserName = Wallet,
            WalletAddress = Wallet,
            UserProfileId = null
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private ProfileService CreateService(out AppDbContext context)
    {
        context = new AppDbContext(_options);
        return new ProfileService(
            new UserProfileRepository(context),
            new UpdateUserProfileRequestValidator(),
            new UpdateAvatarRequestValidator());
    }

    private async Task<Guid> SeedAsync(string? wallet = Wallet)
    {
        await using var context = new AppDbContext(_options);
        var profile = new UserProfile
        {
            Email = $"{Guid.NewGuid():N}@wallet.thiscafeteria.local",
            DisplayName = "0x9D53...eceB"
        };
        context.UserProfiles.Add(profile);
        context.Users.Add(new ApplicationUser
        {
            UserName = wallet ?? "no-wallet",
            UserProfileId = profile.Id,
            WalletAddress = wallet
        });
        await context.SaveChangesAsync();
        return profile.Id;
    }
}
