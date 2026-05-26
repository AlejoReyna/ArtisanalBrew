namespace ThisCafeteria.Infrastructure.Configuration;

public sealed class AwsMessagingOptions
{
    public const string SectionName = "AWS";

    public string Region { get; set; } = "us-east-1";
    public string SqsQueueUrl { get; set; } = string.Empty;
    public string ServiceUrl { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public string S3BucketName { get; set; } = string.Empty;
    public string SesSenderEmail { get; set; } = string.Empty;
}
