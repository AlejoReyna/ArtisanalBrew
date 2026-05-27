using Amazon.S3;
using Amazon.SimpleEmailV2;
using Amazon.SQS;
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
        services.AddAwsServices(configuration);

        var connectionString = DatabaseConnectionStringFactory.Resolve(configuration);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services;
        }

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ITransparencyRecordRepository, TransparencyRecordRepository>();
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        services.AddScoped<IRewardClaimRepository, RewardClaimRepository>();
        services.AddScoped<IWalletStatusEventRepository, WalletStatusEventRepository>();
        services.AddScoped<IS3StorageService, S3StorageService>();
        services.AddScoped<IEmailSender, SesEmailSender>();
        services.Configure<CatalogOptions>(configuration.GetSection(CatalogOptions.SectionName));
        services.AddScoped<DatabaseSeeder>();

        return services;
    }

    public static IServiceCollection AddAwsServices(this IServiceCollection services, IConfiguration configuration)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var awsSection = configuration.GetSection(AwsMessagingOptions.SectionName);
        services.Configure<AwsMessagingOptions>(options =>
        {
            var bound = AwsClientFactory.BindOptions(configuration, awsSection);
            options.Region = bound.Region;
            options.SqsQueueUrl = bound.SqsQueueUrl;
            options.ServiceUrl = bound.ServiceUrl;
            options.Profile = bound.Profile;
            options.S3BucketName = bound.S3BucketName;
            options.SesSenderEmail = bound.SesSenderEmail;
        });

        var awsOptions = AwsClientFactory.BindOptions(configuration, awsSection);

        services.AddSingleton(_ => AwsClientFactory.CreateSqsClient(awsOptions));
        services.AddSingleton(_ => AwsClientFactory.CreateS3Client(awsOptions));
        services.AddSingleton(_ => AwsClientFactory.CreateSesClient(awsOptions));

        services.AddScoped<ISqsMessagePublisher, SqsMessagePublisher>();
        services.AddScoped<IReceiptService, ReceiptService>();

        return services;
    }

    public static IServiceCollection AddAwsMessaging(this IServiceCollection services, IConfiguration configuration) =>
        services.AddAwsServices(configuration);
}
