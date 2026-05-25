namespace ThisCafeteria.Application.DTOs;

public sealed record CreateOrderRequest(
    Guid UserProfileId,
    IReadOnlyCollection<CartItemDto> Items);
