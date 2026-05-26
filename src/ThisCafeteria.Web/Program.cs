using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Serilog;
using ThisCafeteria.Application;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Infrastructure;
using ThisCafeteria.Infrastructure.Identity;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Web.Components;
using ThisCafeteria.Web.Configuration;
using ThisCafeteria.Web.Catalog;
using ThisCafeteria.Application.Services.Rewards;
using ThisCafeteria.Web.Services.Blockchain;
using ThisCafeteria.Web.Services.Rewards;
using ThisCafeteria.Web.Services.Cart;
using ThisCafeteria.Infrastructure.Configuration;

LocalDotEnvLoader.LoadIfPresent();

var builder = WebApplication.CreateBuilder(args);
var hasDatabase = !string.IsNullOrWhiteSpace(DatabaseConnectionStringFactory.Resolve(builder.Configuration));

builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpContextAccessor();
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
builder.Services.AddScoped<IRewardClaimService, RewardClaimService>();
builder.Services.AddScoped<IShoppingCartService, ShoppingCartService>();
builder.Services.AddScoped<ICartMutationClient, CartMutationClient>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

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

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (hasDatabase)
{
    await AdminIdentitySeeder.SeedAsync(app.Services, app.Configuration);
}

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
