namespace ThisCafeteria.Worker;

public sealed class OrderProcessingWorker(ILogger<OrderProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Order processing worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("SQS polling placeholder: no messages consumed yet");
            logger.LogInformation("Simulated order processing completed at {Timestamp}", DateTimeOffset.UtcNow);

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
