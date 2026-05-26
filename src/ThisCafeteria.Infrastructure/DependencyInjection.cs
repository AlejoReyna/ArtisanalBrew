using Amazon;
using Amazon.Runtime.CredentialManagement;
using Amazon.SQS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Infrastructure.Configuration;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Infrastructure.Persistence.Repositories;
using ThisCafeteria.Infrastructure.Services;

namespace ThisCafeteria.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAwsMessaging(configuration);

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
        services.AddScoped<DatabaseSeeder>();

        return services;
    }

    public static IServiceCollection AddAwsMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var awsSection = configuration.GetSection(AwsMessagingOptions.SectionName);
        services.Configure<AwsMessagingOptions>(options =>
        {
            options.Region = configuration["AWS_REGION"] ?? awsSection["Region"] ?? "us-east-1";
            options.SqsQueueUrl = configuration["SQS_QUEUE_URL"] ?? awsSection["SqsQueueUrl"] ?? string.Empty;
            options.ServiceUrl = configuration["AWS_SERVICE_URL"] ?? awsSection["ServiceUrl"] ?? string.Empty;
            options.Profile = configuration["AWS_PROFILE"] ?? awsSection["Profile"] ?? string.Empty;
        });

        services.AddSingleton<IAmazonSQS>(_ =>
        {
            var options = new AwsMessagingOptions
            {
                Region = configuration["AWS_REGION"] ?? awsSection["Region"] ?? "us-east-1",
                SqsQueueUrl = configuration["SQS_QUEUE_URL"] ?? awsSection["SqsQueueUrl"] ?? string.Empty,
                ServiceUrl = configuration["AWS_SERVICE_URL"] ?? awsSection["ServiceUrl"] ?? string.Empty,
                Profile = configuration["AWS_PROFILE"] ?? awsSection["Profile"] ?? string.Empty
            };

            var config = new AmazonSQSConfig();
            if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            {
                config.ServiceURL = options.ServiceUrl;
            }
            else
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
            }

            if (!string.IsNullOrWhiteSpace(options.Profile))
            {
                var profileStore = new CredentialProfileStoreChain();
                if (profileStore.TryGetAWSCredentials(options.Profile, out var credentials))
                {
                    return new AmazonSQSClient(credentials, config);
                }
            }

            return new AmazonSQSClient(config);
        });
        services.AddScoped<ISqsMessagePublisher, SqsMessagePublisher>();

        return services;
    }
}
