using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Domain.Entities;

public sealed class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OrderNumber { get; set; } = string.Empty;
    public Guid UserProfileId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string WalletAddress { get; set; } = string.Empty;
    public string? PaymentTransactionHash { get; set; }
    public int? PaymentChainId { get; set; }
    public string? PaymentNetworkName { get; set; }
    public decimal? PaymentEthAmount { get; set; }
    public string? PaymentExplorerUrl { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public List<TransparencyRecord> TransparencyRecords { get; set; } = [];

    public UserProfile? UserProfile { get; set; }
    public Receipt? Receipt { get; set; }
}
