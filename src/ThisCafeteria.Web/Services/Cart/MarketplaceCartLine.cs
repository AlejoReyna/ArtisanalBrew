namespace ThisCafeteria.Web.Services.Cart;

public sealed record MarketplaceCartLine(
    string Slug,
    string Name,
    string ImageClass,
    decimal UnitPrice,
    int Quantity,
    string? ImageUrl = null,
    Guid ProductId = default)
{
    public decimal LineTotal => UnitPrice * Quantity;
}
