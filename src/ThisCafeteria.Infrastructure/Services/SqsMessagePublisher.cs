using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ThisCafeteria.Infrastructure.Configuration;

namespace ThisCafeteria.Infrastructure.Services;

public sealed class SqsMessagePublisher(
    AmazonSQSClient sqs,
    IOptions<AwsMessagingOptions> options,
    ILogger<SqsMessagePublisher> logger) : ISqsMessagePublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string?> PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
    {
        var queueUrl = options.Value.SqsQueueUrl;
        if (string.IsNullOrWhiteSpace(queueUrl))
        {
            logger.LogWarning(
                "SQS status publish skipped for {MessageType}; SQS_QUEUE_URL or AWS:SqsQueueUrl is not configured.",
                typeof(TMessage).Name);
            return null;
        }

        var request = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = JsonSerializer.Serialize(message, JsonOptions),
            MessageAttributes =
            {
                ["messageType"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = typeof(TMessage).Name
                }
            }
        };

        if (queueUrl.EndsWith(".fifo", StringComparison.OrdinalIgnoreCase))
        {
            request.MessageGroupId = typeof(TMessage).Name;
            request.MessageDeduplicationId = Guid.NewGuid().ToString("N");
        }

        var response = await sqs.SendMessageAsync(request, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Published {MessageType} status message to SQS. MessageId={MessageId}",
            typeof(TMessage).Name,
            response.MessageId);
        return response.MessageId;
    }
}
