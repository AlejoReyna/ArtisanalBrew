using Azure.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Configuration;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Infrastructure.Persistence.Repositories;
using ThisCafeteria.Infrastructure.Services;

namespace ThisCafeteria.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAzureServices(configuration);

        var connectionString = DatabaseConnectionStringFactory.Resolve(configuration);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services;
        }

        services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<AppDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICouponRepository, CouponRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ITransparencyRecordRepository, TransparencyRecordRepository>();
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        services.AddScoped<IRewardClaimRepository, RewardClaimRepository>();
        services.AddScoped<IWalletStatusEventRepository, WalletStatusEventRepository>();
        services.Configure<CatalogOptions>(configuration.GetSection(CatalogOptions.SectionName));
        services.AddScoped<DatabaseSeeder>();

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

        services.AddScoped<IS3StorageService, S3StorageService>();
        services.AddScoped<IEmailSender, SesEmailSender>();
        services.AddScoped<ISqsMessagePublisher, SqsMessagePublisher>();
        services.AddScoped<IReceiptService, ReceiptService>();

        return services;
    }
}
