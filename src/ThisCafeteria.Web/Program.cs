using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Serilog;
using ThisCafeteria.Application;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Infrastructure;
using ThisCafeteria.Infrastructure.Identity;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Infrastructure.Services;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Web.Components;
using ThisCafeteria.Web.Configuration;
using ThisCafeteria.Web.Catalog;
using ThisCafeteria.Application.Services.Rewards;
using ThisCafeteria.Web.Services.Blockchain;
using ThisCafeteria.Web.Services.Rewards;
using ThisCafeteria.Web.Services.Cart;
using ThisCafeteria.Web.Services.Wallet;
using ThisCafeteria.Web.HealthChecks;
using ThisCafeteria.Infrastructure.Configuration;

LocalDotEnvLoader.LoadIfPresent();

var builder = WebApplication.CreateBuilder(args);
var hasDatabase = !string.IsNullOrWhiteSpace(DatabaseConnectionStringFactory.Resolve(builder.Configuration));
var blockchainOptions = builder.Configuration.GetSection(BlockchainOptions.SectionName).Get<BlockchainOptions>() ?? BlockchainOptions.CreateDefaults();
if (blockchainOptions.Chains.Count == 0)
{
    blockchainOptions = BlockchainOptions.CreateDefaults();
}
blockchainOptions = BlockchainManifestLoader.LoadDeploymentManifests(
    blockchainOptions,
    builder.Configuration["Blockchain:LocalEvmManifest"] ?? Environment.GetEnvironmentVariable("ARTISANALBREW_EVM_MANIFEST"),
    builder.Configuration["Blockchain:SolanaDeploymentManifest"] ?? builder.Configuration["Blockchain:LocalSolanaManifest"] ?? Environment.GetEnvironmentVariable("ARTISANALBREW_SOLANA_MANIFEST"));

builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
// AddControllersWithViews (not bare AddControllers) is required: [ValidateAntiForgeryToken]
// resolves ValidateAntiforgeryTokenAuthorizationFilter from DI, which is only registered by
// the MVC "views" feature set. With bare AddControllers, every [ValidateAntiForgeryToken]
// action (stake, unstake, claim, wallet session, coupon) 500s with "No service for type
// ValidateAntiforgeryTokenAuthorizationFilter has been registered".
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<MigrationReadinessHealthCheck>("database-initialization", tags: ["ready"]);
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/javascript", "application/json", "text/css", "text/html" });
});
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

var azureOptions = builder.Configuration.GetSection(AzureOptions.SectionName).Get<AzureOptions>() ?? new AzureOptions();
if (!string.IsNullOrWhiteSpace(azureOptions.Storage.BlobEndpoint))
{
    // Container Apps runs multiple replicas with no shared filesystem. Without a shared key
    // ring, each replica encrypts session/antiforgery cookies with its own in-memory keys, so
    // any request the load balancer routes to a different replica (or after any replica
    // restarts) fails to decrypt them — surfacing to users as a Blazor circuit crash.
    var dataProtectionCredential = AzureClientFactory.CreateCredential(azureOptions.ManagedIdentity);
    var keysBlobUri = new Uri($"{azureOptions.Storage.BlobEndpoint.TrimEnd('/')}/dataprotection-keys/keys.xml");

    builder.Services.AddDataProtection()
        .SetApplicationName("ThisCafeteria")
        .PersistKeysToAzureBlobStorage(keysBlobUri, dataProtectionCredential);
}

builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(blockchainOptions);
builder.Services.AddSingleton<IChainRegistry>(new ChainRegistry(blockchainOptions));
builder.Services.AddScoped<ISelectedChainAccessor, SelectedChainAccessor>();
var blockchainNetworkSection = builder.Configuration.GetSection(BlockchainNetworkOptions.SectionName);
if (!blockchainNetworkSection.Exists())
{
    blockchainNetworkSection = builder.Configuration.GetSection(BlockchainNetworkOptions.LegacySectionName);
}

builder.Services.Configure<BlockchainNetworkOptions>(blockchainNetworkSection);
builder.Services.AddSingleton(serviceProvider =>
    serviceProvider.GetRequiredService<IOptions<BlockchainNetworkOptions>>().Value);
builder.Services.Configure<CoffeeCoinOwnerOptions>(
    builder.Configuration.GetSection(CoffeeCoinOwnerOptions.SectionName));
builder.Services.AddSingleton<ICoffeeWeb3Service, CoffeeWeb3Service>();
builder.Services.AddScoped<EvmLiquidStakingGateway>();
builder.Services.AddScoped<SolanaLiquidStakingGateway>();
builder.Services.AddScoped<ILiquidStakingGateway, MultichainLiquidStakingGateway>();
builder.Services.AddSingleton<IWalletChallengeService, WalletChallengeService>();
builder.Services.AddScoped<ISolanaWalletChallengeService, SolanaWalletChallengeService>();
builder.Services.AddScoped<WalletDashboardState>();
builder.Services.AddHttpClient<IEthUsdPriceService, CoinGeckoEthUsdPriceService>(client =>
{
    client.BaseAddress = new Uri("https://api.coingecko.com/api/v3/");
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddScoped<IRewardClaimService, RewardClaimService>();
builder.Services.AddScoped<IShoppingCartService, ShoppingCartService>();
builder.Services.AddScoped<ICartMutationClient, CartMutationClient>();
builder.Services.AddScoped<IAgenticJobService, AgenticJobService>();
builder.Services.AddScoped<ISmartAccountService, SmartAccountService>();
// Sponsorship policy. Absent configuration yields a disabled (fail-closed) policy.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(builder.Configuration
    .GetSection(SponsorshipPolicyOptions.SectionName)
    .Get<SponsorshipPolicyOptions>() ?? new SponsorshipPolicyOptions());
builder.Services.AddScoped<ISponsorshipPolicyService, SponsorshipPolicyService>();
builder.Services.AddScoped<IUserOperationSponsor, UserOperationSponsor>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IMigrationReadiness, MigrationReadiness>();
builder.Services.AddHostedService<MigrationHostedService>(provider =>
    new MigrationHostedService(
        provider.GetRequiredService<IServiceProvider>(),
        provider.GetRequiredService<IConfiguration>(),
        provider.GetRequiredService<IMigrationReadiness>(),
        provider.GetRequiredService<ILogger<MigrationHostedService>>(),
        hasDatabase));

if (hasDatabase)
{
    builder.Services
        .AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

    builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, ApplicationUserClaimsPrincipalFactory>();

    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.LoginPath = "/";
        options.AccessDeniedPath = "/access-denied";
    });
}
else
{
    builder.Services
        .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/";
            options.AccessDeniedPath = "/access-denied";
            options.Cookie.Name = "ThisCafeteria.Wallet";
        });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
else
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseResponseCompression();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.Use((context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
    {
        return next(context);
    }

    var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
    {
        HttpOnly = false,
        SameSite = SameSiteMode.Strict,
        Secure = context.Request.IsHttps
    });

    return next(context);
});

app.MapStaticAssets();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
// Keep the original endpoint as a backwards-compatible readiness check.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

ValidateMarketplaceCatalog(app.Services);

app.Run();

static void ValidateMarketplaceCatalog(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var slugs = MarketplaceCatalog.Summaries.Select(summary => summary.Slug).ToList();
        var duplicateSlugs = slugs
            .GroupBy(slug => slug, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateSlugs.Count > 0)
        {
            logger.LogError("Marketplace catalog has duplicate slugs: {DuplicateSlugs}", string.Join(", ", duplicateSlugs));
        }

        foreach (var summary in MarketplaceCatalog.Summaries)
        {
            var detail = MarketplaceCatalog.TryGetBySlug(summary.Slug);
            if (detail is null)
            {
                logger.LogError(
                    "Marketplace catalog missing detail for {Name} (slug {Slug})",
                    summary.Name,
                    summary.Slug);
            }
        }

        logger.LogInformation("Marketplace catalog validated {ProductCount} products", slugs.Count);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Marketplace catalog validation failed at startup");
    }
}

public partial class Program;

namespace ThisCafeteria.Web
{
    public class WebMarker { }
}
