using System.Text.Json;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ThisCafeteria.Infrastructure.Configuration;

namespace ThisCafeteria.Infrastructure.Services;

// Backs the AWS-flavored ISqsMessagePublisher contract (used by the wallet-status feature)
// with Azure Service Bus.
public sealed class SqsMessagePublisher(
    IOptions<AzureOptions> options,
    TokenCredential credential,
    ILogger<SqsMessagePublisher> logger) : ISqsMessagePublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string?> PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
    {
        var serviceBusOptions = options.Value.ServiceBus;
        if (string.IsNullOrWhiteSpace(serviceBusOptions.FullyQualifiedNamespace))
        {
            logger.LogWarning(
                "Service Bus publish skipped for {MessageType}; Azure:ServiceBus:FullyQualifiedNamespace is not configured.",
                typeof(TMessage).Name);
            return null;
        }

        try
        {
            await using var client = new ServiceBusClient(serviceBusOptions.FullyQualifiedNamespace, credential);
            await using var sender = client.CreateSender(serviceBusOptions.WalletStatusQueueName);

            var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
            var serviceBusMessage = new ServiceBusMessage(body)
            {
                MessageId = Guid.NewGuid().ToString("N"),
                ApplicationProperties = { ["messageType"] = typeof(TMessage).Name }
            };

            await sender.SendMessageAsync(serviceBusMessage, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Published {MessageType} status message to Service Bus. MessageId={MessageId}",
                typeof(TMessage).Name,
                serviceBusMessage.MessageId);
            return serviceBusMessage.MessageId;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Service Bus publish skipped for {MessageType}; the message could not be sent.",
                typeof(TMessage).Name);
            return null;
        }
    }
}
