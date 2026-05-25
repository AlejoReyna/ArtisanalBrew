using ThisCafeteria.Application.DTOs;

namespace ThisCafeteria.Application.Services;

public interface IOrderService
{
    Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<OrderDto>> GetOrdersForUserAsync(Guid userProfileId, CancellationToken cancellationToken = default);
}
