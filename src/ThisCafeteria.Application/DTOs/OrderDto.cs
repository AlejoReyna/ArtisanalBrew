using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Application.DTOs;

public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    Guid UserProfileId,
    OrderStatus Status,
    decimal Subtotal,
    decimal Tax,
    decimal Total,
    DateTime CreatedAt,
    IReadOnlyCollection<CartItemDto> Items);
