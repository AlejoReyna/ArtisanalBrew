using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Domain.Entities;

public sealed class Order
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string OrderNumber { get; private set; } = string.Empty;
    public Guid UserProfileId { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public decimal Subtotal { get; private set; }
    public decimal Shipping { get; private set; }
    public decimal Tax { get; private set; }
    public Guid? CouponId { get; private set; }
    public string? CouponCode { get; private set; }
    public decimal? CouponDiscountPercent { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal Total { get; private set; }
    public string WalletAddress { get; private set; } = string.Empty;
    public string? PaymentTransactionHash { get; private set; }
    public int? PaymentChainId { get; private set; }
    public string? PaymentNetworkName { get; private set; }
    public decimal? PaymentEthAmount { get; private set; }
    public string? PaymentExplorerUrl { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }
    public List<OrderItem> Items { get; private set; } = [];
    public List<TransparencyRecord> TransparencyRecords { get; set; } = [];

    public UserProfile? UserProfile { get; set; }
    public Receipt? Receipt { get; set; }
    public Coupon? Coupon { get; set; }
    public CouponRedemption? CouponRedemption { get; set; }

    private Order() { }

    public static Order Place(
        Guid userProfileId,
        string orderNumber,
        string walletAddress,
        IEnumerable<OrderItem> items,
        OrderPricing pricing,
        Coupon? coupon,
        DateTime? createdAtUtc = null)
    {
        if (userProfileId == Guid.Empty || string.IsNullOrWhiteSpace(orderNumber) || string.IsNullOrWhiteSpace(walletAddress))
        {
            throw new InvalidOperationException("Order identity is required.");
        }

        var order = new Order
        {
            UserProfileId = userProfileId,
            OrderNumber = orderNumber,
            WalletAddress = walletAddress,
            Status = OrderStatus.Processing,
            Subtotal = pricing.Subtotal,
            Shipping = pricing.Shipping,
            Tax = pricing.Tax,
            DiscountAmount = pricing.DiscountAmount,
            Total = pricing.Total,
            CouponId = coupon?.Id,
            CouponCode = coupon?.Code,
            CouponDiscountPercent = coupon?.DiscountPercent,
            CreatedAt = createdAtUtc ?? DateTime.UtcNow,
            Items = items.ToList()
        };

        return order;
    }

    public void RecordPayment(
        string? transactionHash,
        int? chainId,
        string? networkName,
        decimal? ethAmount,
        string? explorerUrl,
        DateTime? paidAtUtc)
    {
        PaymentTransactionHash = transactionHash;
        PaymentChainId = chainId;
        PaymentNetworkName = networkName;
        PaymentEthAmount = ethAmount;
        PaymentExplorerUrl = explorerUrl;
        PaidAtUtc = paidAtUtc;
        UpdatedAt = DateTime.UtcNow;
    }

    public void TransitionTo(OrderStatus nextStatus)
    {
        var allowed = (Status, nextStatus) switch
        {
            (OrderStatus.Pending, OrderStatus.Processing or OrderStatus.Cancelled) => true,
            (OrderStatus.Processing, OrderStatus.Completed or OrderStatus.Cancelled) => true,
            _ => false
        };

        if (!allowed)
        {
            throw new InvalidOperationException($"Cannot transition order from {Status} to {nextStatus}.");
        }

        Status = nextStatus;
        UpdatedAt = DateTime.UtcNow;
    }
}
