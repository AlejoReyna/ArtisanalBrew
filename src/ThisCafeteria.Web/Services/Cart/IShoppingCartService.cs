using ThisCafeteria.Application.DTOs;

namespace ThisCafeteria.Web.Services.Cart;

public interface IShoppingCartService
{
    event Action? Changed;

    Task<IReadOnlyList<MarketplaceCartLine>> GetLinesAsync(CancellationToken cancellationToken = default);

    Task<int> GetItemCountAsync(CancellationToken cancellationToken = default);

    Task<decimal> GetSubtotalAsync(CancellationToken cancellationToken = default);

    Task AddAsync(string slug, int quantity = 1, CancellationToken cancellationToken = default);

    Task SetQuantityAsync(string slug, int quantity, CancellationToken cancellationToken = default);

    Task RemoveAsync(string slug, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CartItemDto>> ToOrderItemsAsync(CancellationToken cancellationToken = default);

    Task RefreshFromStorageAsync(CancellationToken cancellationToken = default);

    void ApplyCircuitSnapshot(IReadOnlyList<MarketplaceCartLine> lines);
}


