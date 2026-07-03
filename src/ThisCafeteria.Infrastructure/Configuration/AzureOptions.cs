namespace ThisCafeteria.Infrastructure.Configuration;

public sealed class AzureOptions
{
    public const string SectionName = "Azure";

    public AzureManagedIdentityOptions ManagedIdentity { get; set; } = new();
    public AzureStorageOptions Storage { get; set; } = new();
    public AzureServiceBusOptions ServiceBus { get; set; } = new();
    public AzureCommunicationOptions Communication { get; set; } = new();
}

public sealed class AzureManagedIdentityOptions
{
    public string ClientId { get; set; } = string.Empty;
}

public sealed class AzureStorageOptions
{
    public string BlobEndpoint { get; set; } = string.Empty;
    public string ReceiptsContainerName { get; set; } = "receipts";
}

public sealed class AzureServiceBusOptions
{
    public string FullyQualifiedNamespace { get; set; } = string.Empty;
    public string WalletStatusQueueName { get; set; } = "wallet-status";
    public string OrderProcessingQueueName { get; set; } = "order-processing";
}

public sealed class AzureCommunicationOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string SenderAddress { get; set; } = string.Empty;
}
