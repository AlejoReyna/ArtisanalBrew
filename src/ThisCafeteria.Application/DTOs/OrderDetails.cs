namespace ThisCafeteria.Application.DTOs;

public sealed class OrderDetails
{
    public required string OrderId { get; init; }
    public required string CustomerEmail { get; init; }
    public required string CustomerName { get; init; }
    public DateTimeOffset PurchaseDate { get; init; }
    public required IReadOnlyList<OrderItemDetails> Items { get; init; }
    public decimal Subtotal { get; init; }
    public decimal Shipping { get; init; }
    public decimal Tax { get; init; }
    public string? CouponCode { get; init; }
    public decimal? CouponDiscountPercent { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal Total { get; init; }
}

public sealed class OrderItemDetails
{
    public required string Name { get; init; }
    public int Qty { get; init; }
    public decimal Price { get; init; }
}
