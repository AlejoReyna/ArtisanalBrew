using Azure.Core;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ThisCafeteria.Infrastructure.Configuration;

namespace ThisCafeteria.Infrastructure.Services;

/// <summary>
/// Infrastructure adapter that receives order-processing messages from Azure Service Bus.
/// The Worker host only composes this hosted service.
/// </summary>
public sealed class OrderProcessingWorker(
    IOptions<AzureOptions> options,
    TokenCredential credential,
    ILogger<OrderProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serviceBusOptions = options.Value.ServiceBus;
        if (string.IsNullOrWhiteSpace(serviceBusOptions.FullyQualifiedNamespace))
        {
            logger.LogWarning("Order processing worker idle; Azure:ServiceBus:FullyQualifiedNamespace is not configured.");
            return;
        }

        await using var client = new ServiceBusClient(serviceBusOptions.FullyQualifiedNamespace, credential);
        await using var processor = client.CreateProcessor(
            serviceBusOptions.OrderProcessingQueueName,
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 1
            });

        processor.ProcessMessageAsync += async args =>
        {
            logger.LogInformation(
                "Processing order message. MessageId={MessageId}, Body={Body}",
                args.Message.MessageId,
                args.Message.Body.ToString());

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        };

        processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "Error processing Service Bus message from {EntityPath}", args.EntityPath);
            return Task.CompletedTask;
        };

        logger.LogInformation(
            "Order processing worker started, listening on queue {QueueName}",
            serviceBusOptions.OrderProcessingQueueName);

        await processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on graceful shutdown.
        }

        await processor.StopProcessingAsync(CancellationToken.None);
    }
}
