using Microsoft.Extensions.Logging;

namespace ThisCafeteria.Infrastructure.Services;

public sealed class SqsMessagePublisher(ILogger<SqsMessagePublisher> logger) : ISqsMessagePublisher
{
    public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("SQS publish placeholder for message type {MessageType}", typeof(TMessage).Name);
        return Task.CompletedTask;
    }
}
