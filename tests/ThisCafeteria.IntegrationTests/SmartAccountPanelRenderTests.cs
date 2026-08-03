using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Identity;
using Xunit;

namespace ThisCafeteria.IntegrationTests;

/// <summary>
/// Renders the authenticated /profile page end-to-end against the real Postgres fixture to prove
/// the SmartAccountPanel component (smart-account discovery/selection UI) mounts and renders
/// without a server error. Stubs ISmartAccountService to always report "not configured" for the
/// selected chain, so this exercises the RPC-free fail-closed empty-state path deterministically
/// and offline regardless of which chains have ERC-4337 infrastructure wired up in the real
/// deployment manifests (ethereum-sepolia now does - see ethereum-sepolia.json's accountFactory),
/// matching this repo's existing unit-test posture around SmartAccountService (see
/// SmartAccountServiceTests) rather than depending on a live chain.
/// </summary>
public sealed class SmartAccountPanelRenderTests : IClassFixture<ThisCafeteriaWebApplicationFactory>, IAsyncLifetime
{
    private const string TestUserIdHeader = "X-Test-User-Id";
    private readonly ThisCafeteriaWebApplicationFactory _baseFactory;
    private WebApplicationFactory<Program>? _factory;
    private string _userId = string.Empty;

    public SmartAccountPanelRenderTests(ThisCafeteriaWebApplicationFactory factory)
    {
        _baseFactory = factory;
    }

    public async Task InitializeAsync()
    {
        _factory = _baseFactory.WithWebHostBuilder(builder =>
        {
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
                services.RemoveAll<ISmartAccountService>();
                services.AddScoped<ISmartAccountService, NotConfiguredSmartAccountService>();
            });
        });

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"smart-account-panel-{Guid.NewGuid():N}@example.local";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            // Random per-run address (AspNetUsers.WalletAddress has a unique index) so repeated
            // test runs against the same shared Postgres fixture never collide.
            WalletAddress = $"0x{Guid.NewGuid():N}{Guid.NewGuid():N}"[..42]
        };
        var result = await userManager.CreateAsync(user);
        result.Succeeded.Should().BeTrue(string.Join(",", result.Errors.Select(e => e.Description)));
        _userId = user.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ProfilePage_RendersSmartAccountPanel_WithoutServerError()
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Add(TestUserIdHeader, _userId);

        var response = await client.GetAsync("/profile");
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue($"expected /profile to render, got {(int)response.StatusCode}: {body}");
        body.Should().Contain("Smart Accounts", "the panel's section kicker should be present");
        body.Should().Contain("chain-selector", "the panel should embed a chain picker so the user can inspect other chains");
        body.Should().Contain(
            "No ERC-4337 smart account is configured",
            "the default chain has no ERC-4337 factory configured, so the fail-closed empty state must render instead of an error");
        body.Should().NotContain("smart-account-panel__error", "no chain-read error should occur on the RPC-free default chain");
    }

    /// <summary>Fails closed for every chain, exercising the same path a real unconfigured chain takes.</summary>
    private sealed class NotConfiguredSmartAccountService : ISmartAccountService
    {
        public Task<bool> IsConfiguredAsync(string chainKey) => Task.FromResult(false);

        public Task<string> GetOrDeployAccountAsync(string chainKey, string ownerAddress) =>
            throw new NotSupportedException("Smart accounts are not configured for this chain.");

        public Task<bool> HasSufficientSponsorshipQuotaAsync(string chainKey, string ownerAddress, decimal estimatedCostUsd) =>
            Task.FromResult(false);

        public Task RecordSponsorshipUsageAsync(string chainKey, string ownerAddress, decimal costUsd) =>
            Task.CompletedTask;

        public Task RevokeSessionPermissionsAsync(string chainKey, string ownerAddress) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SmartAccountInfo>> DiscoverAccountsAsync(string chainKey, string ownerAddress) =>
            throw new NotSupportedException("Smart accounts are not configured for this chain.");

        public Task<SmartAccountInfo> RegisterModularAccountAsync(string chainKey, string ownerAddress, string accountAddress, string salt) =>
            throw new NotSupportedException("Smart accounts are not configured for this chain.");

        public Task<string> SubmitOwnerUserOperationAsync(string chainKey, string ownerAddress, BundlerUserOperation operation, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smart accounts are not configured for this chain.");

        public Task<BundlerGasEstimate> EstimateUserOperationGasAsync(string chainKey, BundlerUserOperation operation, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Smart accounts are not configured for this chain.");

        public Task<AgentPermissionEpochInfo?> GetActivePermissionEpochAsync(string chainKey, string delegatorAddress) =>
            Task.FromResult<AgentPermissionEpochInfo?>(null);

        public Task<AgentPermissionEpochInfo> RecordPermissionEpochInstalledAsync(
            string chainKey,
            string delegatorAddress,
            string agentAddress,
            string epoch,
            DateTime validAfterUtc,
            DateTime validBeforeUtc,
            string installedTxHash,
            IReadOnlyList<AgentPermissionGrantInput> grants) =>
            throw new NotSupportedException("Smart accounts are not configured for this chain.");
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "TestScheme";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(TestUserIdHeader, out var userId) || string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Name, userId.ToString()) };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
