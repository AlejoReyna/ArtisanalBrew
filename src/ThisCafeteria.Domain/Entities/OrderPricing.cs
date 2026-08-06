namespace ThisCafeteria.Domain.Entities;

/// <summary>Immutable money calculation owned by the order domain.</summary>
public sealed record OrderPricing(
    decimal Subtotal,
    decimal Shipping,
    decimal Tax,
    decimal TotalBeforeDiscount,
    decimal DiscountAmount,
    decimal Total)
{
    public static OrderPricing Calculate(
        IEnumerable<OrderItem> items,
        decimal shippingAmount,
        decimal taxRate,
        decimal? discountPercent = null)
    {
        var materializedItems = items.ToArray();
        if (materializedItems.Any(item => item.Quantity <= 0 || item.UnitPrice < 0m))
        {
            throw new InvalidOperationException("Order items must have a positive quantity and non-negative unit price.");
        }

        if (shippingAmount < 0m || taxRate < 0m || discountPercent is < 0m or > 100m)
        {
            throw new InvalidOperationException("Order pricing inputs are invalid.");
        }

        var subtotal = materializedItems.Sum(item => item.UnitPrice * item.Quantity);
        var tax = decimal.Round(subtotal * taxRate, 2, MidpointRounding.AwayFromZero);
        var beforeDiscount = subtotal + shippingAmount + tax;
        var discount = decimal.Round(
            beforeDiscount * (discountPercent ?? 0m) / 100m,
            2,
            MidpointRounding.AwayFromZero);

        return new OrderPricing(subtotal, shippingAmount, tax, beforeDiscount, discount, Math.Max(0m, beforeDiscount - discount));
    }
}
