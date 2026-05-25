namespace ThisCafeteria.Domain.Entities;

public sealed class TransparencyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Total { get; set; }
    public string OrderHash { get; set; } = string.Empty;
    public int ChainId { get; set; }
    public string NetworkName { get; set; } = string.Empty;
    public string ContractAddress { get; set; } = string.Empty;
    public string TransactionHash { get; set; } = string.Empty;
    public string ExplorerUrl { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RecordedOnChainAt { get; set; }

    public Order? Order { get; set; }
}
