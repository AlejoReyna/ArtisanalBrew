using FluentValidation;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Application.Services;

public sealed class OrderService(
    IOrderRepository orderRepository,
    IValidator<CreateOrderRequest> validator) : IOrderService
{
    private const decimal TaxRate = 0.16m;

    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var subtotal = request.Items.Sum(item => item.UnitPrice * item.Quantity);
        var tax = decimal.Round(subtotal * TaxRate, 2, MidpointRounding.AwayFromZero);
        var order = new Order
        {
            UserProfileId = request.UserProfileId,
            OrderNumber = $"TC-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}",
            Status = OrderStatus.Pending,
            Subtotal = subtotal,
            Tax = tax,
            Total = subtotal + tax,
            CreatedAt = DateTime.UtcNow,
            Items = request.Items.Select(item => new OrderItem
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Total = item.UnitPrice * item.Quantity
            }).ToList()
        };

        await orderRepository.AddAsync(order, cancellationToken);
        return Map(order);
    }

    public async Task<IReadOnlyCollection<OrderDto>> GetOrdersForUserAsync(Guid userProfileId, CancellationToken cancellationToken = default)
    {
        var orders = await orderRepository.GetOrdersForUserAsync(userProfileId, cancellationToken);
        return orders.Select(Map).ToArray();
    }

    private static OrderDto Map(Order order) => new(
        order.Id,
        order.OrderNumber,
        order.UserProfileId,
        order.Status,
        order.Subtotal,
        order.Tax,
        order.Total,
        order.CreatedAt,
        order.Items.Select(item => new CartItemDto(
            item.ProductId,
            item.ProductName,
            item.Quantity,
            item.UnitPrice)).ToArray());
}
