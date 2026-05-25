namespace ThisCafeteria.Web.Services.Cart;

public sealed record MarketplaceCartLine(
    string Slug,
    string Name,
    string ImageClass,
    decimal UnitPrice,
    int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}
