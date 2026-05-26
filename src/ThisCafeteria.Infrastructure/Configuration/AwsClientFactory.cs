using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.SimpleEmailV2;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;

namespace ThisCafeteria.Infrastructure.Configuration;

internal static class AwsClientFactory
{
    public static AwsMessagingOptions BindOptions(IConfiguration configuration, IConfigurationSection awsSection) =>
        new()
        {
            Region = configuration["AWS_REGION"] ?? awsSection["Region"] ?? "us-east-1",
            SqsQueueUrl = configuration["SQS_QUEUE_URL"] ?? awsSection["SqsQueueUrl"] ?? string.Empty,
            ServiceUrl = configuration["AWS_SERVICE_URL"] ?? awsSection["ServiceUrl"] ?? string.Empty,
            Profile = configuration["AWS_PROFILE"] ?? awsSection["Profile"] ?? string.Empty,
            S3BucketName = configuration["AWS_S3_BUCKET_NAME"] ?? awsSection["S3BucketName"] ?? string.Empty,
            SesSenderEmail = configuration["AWS_SES_SENDER_EMAIL"] ?? awsSection["SesSenderEmail"] ?? string.Empty
        };

    public static AmazonSQSClient CreateSqsClient(AwsMessagingOptions options)
    {
        var config = new AmazonSQSConfig();
        ApplyEndpoint(config, options);
        var credentials = ResolveCredentials(options);
        return credentials is not null
            ? new AmazonSQSClient(credentials, config)
            : new AmazonSQSClient(config);
    }

    public static AmazonS3Client CreateS3Client(AwsMessagingOptions options)
    {
        var config = new AmazonS3Config();
        ApplyEndpoint(config, options);
        var credentials = ResolveCredentials(options);
        return credentials is not null
            ? new AmazonS3Client(credentials, config)
            : new AmazonS3Client(config);
    }

    public static AmazonSimpleEmailServiceV2Client CreateSesClient(AwsMessagingOptions options)
    {
        var config = new AmazonSimpleEmailServiceV2Config();
        ApplyEndpoint(config, options);
        var credentials = ResolveCredentials(options);
        return credentials is not null
            ? new AmazonSimpleEmailServiceV2Client(credentials, config)
            : new AmazonSimpleEmailServiceV2Client(config);
    }

    private static void ApplyEndpoint(ClientConfig config, AwsMessagingOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            return;
        }

        config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
    }

    private static AWSCredentials? ResolveCredentials(AwsMessagingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Profile))
        {
            return null;
        }

        var profileStore = new CredentialProfileStoreChain();
        return profileStore.TryGetAWSCredentials(options.Profile, out var credentials) ? credentials : null;
    }
}
