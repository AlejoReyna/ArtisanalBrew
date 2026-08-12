using Azure.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Application.Services.Rewards;
using ThisCafeteria.Application.Services.Wallet;
using ThisCafeteria.Infrastructure.Configuration;
using ThisCafeteria.Infrastructure.Identity;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Infrastructure.Persistence.Repositories;
using ThisCafeteria.Infrastructure.Services;
using ThisCafeteria.Infrastructure.Services.Blockchain;
using ThisCafeteria.Infrastructure.Services.Rewards;
using ThisCafeteria.Infrastructure.Services.Wallet;

namespace ThisCafeteria.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAzureServices(configuration);

        var connectionString = DatabaseConnectionStringFactory.Resolve(configuration);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Keep the public transparency surface resolvable so /story and /api/transparency
            // answer honestly rather than throwing when no database is wired up.
            services.AddScoped<IDatabaseSchemaService, UnavailableDatabaseSchemaService>();
            return services;
        }

        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });
        services.AddScoped<AppDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAgenticCommerceProjectionBatch, AgenticCommerceProjectionBatch>();
        services.AddScoped<IWalletAuthChallengeRepository, WalletAuthChallengeRepository>();
        services.AddScoped<IWalletIdentityRepository, WalletIdentityRepository>();
        services.AddScoped<ISolanaWalletChallengeService, SolanaWalletChallengeService>();
        services.AddScoped<ISolanaFaucetClaimRepository, SolanaFaucetClaimRepository>();
        services.AddScoped<ISolanaFaucetService, SolanaFaucetService>();
        services.AddScoped<IStakingLedgerRepository, StakingLedgerRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICouponRepository, CouponRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ITransparencyRecordRepository, TransparencyRecordRepository>();
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        services.AddScoped<IRewardClaimRepository, RewardClaimRepository>();
        services.AddScoped<IWalletStatusEventRepository, WalletStatusEventRepository>();
        services.Configure<CatalogOptions>(configuration.GetSection(CatalogOptions.SectionName));
        services.AddScoped<DatabaseSeeder>();

        services.AddMemoryCache();
        services.AddScoped<IDatabaseSchemaService, DatabaseSchemaService>();

        return services;
    }

    /// <summary>
    /// Registers the chain gateways. These have no database dependency - they talk to RPC
    /// endpoints - so they register unconditionally, unlike the repository-backed services above.
    /// </summary>
    public static IServiceCollection AddBlockchainInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CoffeeCoinOwnerOptions>(
            configuration.GetSection(CoffeeCoinOwnerOptions.SectionName));

        services.AddSingleton<ICoffeeWeb3Service, CoffeeWeb3Service>();
        services.AddScoped<EvmLiquidStakingGateway>();
        services.AddScoped<SolanaLiquidStakingGateway>();
        services.AddScoped<ILiquidStakingGateway, MultichainLiquidStakingGateway>();
        services.AddScoped<IMarketplacePaymentGateway, EvmMarketplacePaymentGateway>();

        services.AddHttpClient<IEthUsdPriceService, CoinGeckoEthUsdPriceService>(client =>
        {
            client.BaseAddress = new Uri("https://api.coingecko.com/api/v3/");
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        return services;
    }

    /// <summary>
    /// Registers services that need both a database (<see cref="IRewardClaimRepository"/>,
    /// ASP.NET Core Identity's <c>UserManager</c>) and, for reward claiming, the blockchain
    /// gateway from <see cref="AddBlockchainInfrastructure"/>. These are presentation-triggered
    /// (a user claiming rewards or managing their account) and unused by the standing background
    /// workers, so they live outside <see cref="AddInfrastructure"/> - the Worker host calls that
    /// method but never <see cref="AddBlockchainInfrastructure"/> or ASP.NET Identity's
    /// <c>AddIdentity</c>, and registering these unconditionally there left the Worker's service
    /// provider unable to validate at startup.
    /// </summary>
    public static IServiceCollection AddIdentityAndRewardServices(this IServiceCollection services)
    {
        services.AddScoped<IIdentityAccountService, IdentityAccountService>();
        services.AddScoped<IRewardClaimService, RewardClaimService>();
        return services;
    }

    public static IServiceCollection AddAzureServices(this IServiceCollection services, IConfiguration configuration)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        services.Configure<AzureOptions>(configuration.GetSection(AzureOptions.SectionName));

        services.AddSingleton<TokenCredential>(serviceProvider =>
        {
            var azureOptions = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureOptions>>().Value;
            return AzureClientFactory.CreateCredential(azureOptions.ManagedIdentity);
        });

        services.Configure<ReceiptStorageOptions>(configuration.GetSection(ReceiptStorageOptions.SectionName));
        services.Configure<ResendOptions>(configuration.GetSection(ResendOptions.SectionName));

        services.AddScoped<IS3StorageService, LocalReceiptStorageService>();
        services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.resend.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<ISqsMessagePublisher, SqsMessagePublisher>();
        services.AddScoped<IReceiptService, ReceiptService>();

        return services;
    }
}
