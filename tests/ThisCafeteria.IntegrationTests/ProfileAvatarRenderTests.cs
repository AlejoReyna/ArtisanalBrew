using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ThisCafeteria.Domain.Avatars;
using ThisCafeteria.Infrastructure.Identity;
using ThisCafeteria.Web.Services;
using Xunit;

namespace ThisCafeteria.IntegrationTests;

/// <summary>
/// Renders the authenticated /profile page against the real Postgres fixture
/// and checks the account robot came out of it.
/// </summary>
/// <remarks>
/// The unit tests prove the seed picks the right items and that the sheet
/// arithmetic points at the right columns; this proves those two actually meet
/// on the page — that the wallet's seeded look reaches the markup as the
/// background-position CSS the browser will use, on a profile that has never
/// been edited and therefore has no avatar row at all.
/// </remarks>
public sealed class ProfileAvatarRenderTests(ThisCafeteriaWebApplicationFactory factory)
    : IClassFixture<ThisCafeteriaWebApplicationFactory>, IAsyncLifetime
{
    private const string TestUserIdHeader = "X-Test-User-Id";

    private WebApplicationFactory<Program>? _factory;
    private string _userId = string.Empty;
    private string _wallet = string.Empty;

    public async Task InitializeAsync()
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultSignInScheme = TestAuthHandler.SchemeName;
                });
            }));

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"profile-avatar-{Guid.NewGuid():N}@example.local";

        // Random per run: AspNetUsers.WalletAddress is uniquely indexed and the
        // fixture is shared, so a fixed address collides on the second run.
        _wallet = $"0x{Guid.NewGuid():N}{Guid.NewGuid():N}"[..42];

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            WalletAddress = _wallet
        };

        var result = await userManager.CreateAsync(user);
        result.Succeeded.Should().BeTrue(string.Join(",", result.Errors.Select(error => error.Description)));
        _userId = user.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ProfilePageRendersTheWalletSeededRobot()
    {
        var body = await LoadProfileAsync();

        body.Should().Contain("pf-avatar", "the avatar has to be a button, not a decorative sprite");
        body.Should().Contain("Change your robot", "the button needs an accessible name");

        // Every sprite layer the seed calls for must be present, pointing at
        // its own column. This is the join: AvatarSeed picked the items,
        // AvatarSheetStyle turned them into CSS, the page emitted both.
        var seeded = AvatarSeed.FromWallet(_wallet);
        foreach (var slot in AvatarCatalog.Slots.Where(s => s.Kind == AvatarSlotKind.Sprite))
        {
            var item = slot.Resolve(seeded[slot.Key]);
            if (item.IsEmpty)
            {
                continue;
            }

            body.Should().Contain(
                AvatarSheetStyle.Layer(slot, item),
                "slot '{0}' seeded to '{1}', so its layer must be on the page", slot.Key, item.Id);
        }

        body.Should().Contain($"data-backdrop=\"{seeded.Backdrop}\"");
    }

    [Fact]
    public async Task AnUneditedProfileStillGetsARobot()
    {
        // The account was created moments ago and has never opened the editor,
        // so UserProfiles.Avatar is NULL. Rendering anything at all here is the
        // whole point of seeding from the wallet instead of defaulting.
        var body = await LoadProfileAsync();

        body.Should().Contain("rbt__layer", "a never-edited profile must still draw a robot");
        body.Should().NotContain("data-backdrop=\"\"");
    }

    private async Task<string> LoadProfileAsync()
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add(TestUserIdHeader, _userId);

        var response = await client.GetAsync("/profile");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue(
            $"expected /profile to render, got {(int)response.StatusCode}: {body}");

        return body;
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestScheme";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(TestUserIdHeader, out var userId) ||
                string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, userId.ToString())
            };

            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
