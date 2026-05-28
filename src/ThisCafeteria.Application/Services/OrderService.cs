using FluentValidation;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Application.Services;

public sealed class OrderService(
    IOrderRepository orderRepository,
    ICouponRepository couponRepository,
    IOrderPricingService pricingService,
    ITransparencyService transparencyService,
    IValidator<CreateOrderRequest> validator) : IOrderService
{
    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var coupon = await ResolveCouponAsync(request, cancellationToken);
        var pricing = pricingService.Calculate(request.Items, coupon);
        var order = new Order
        {
            UserProfileId = request.UserProfileId,
            OrderNumber = $"TC-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}",
            Status = OrderStatus.Processing,
            Subtotal = pricing.Subtotal,
            Shipping = pricing.Shipping,
            Tax = pricing.Tax,
            CouponId = coupon?.Id,
            CouponCode = coupon?.Code,
            CouponDiscountPercent = coupon?.DiscountPercent,
            DiscountAmount = pricing.DiscountAmount,
            Total = pricing.Total,
            WalletAddress = request.WalletAddress,
            PaymentTransactionHash = request.PaymentTransactionHash,
            PaymentChainId = request.PaymentChainId,
            PaymentNetworkName = request.PaymentNetworkName,
            PaymentEthAmount = request.PaymentEthAmount,
            PaymentExplorerUrl = request.PaymentExplorerUrl,
            PaidAtUtc = request.PaidAtUtc,
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

        if (coupon is not null)
        {
            order.CouponRedemption = new CouponRedemption
            {
                CouponId = coupon.Id,
                UserProfileId = request.UserProfileId,
                OrderId = order.Id,
                RedeemedAtUtc = DateTime.UtcNow
            };
        }

        await orderRepository.AddAsync(order, cancellationToken);
        await transparencyService.CreatePendingRecordsForOrderAsync(order, cancellationToken);
        return Map(order);
    }

    public async Task<IReadOnlyCollection<OrderDto>> GetOrdersForUserAsync(Guid userProfileId, CancellationToken cancellationToken = default)
    {
        var orders = await orderRepository.GetOrdersForUserAsync(userProfileId, cancellationToken);
        return orders.Select(Map).ToArray();
    }

    public async Task<IReadOnlyCollection<CommerceTransactionDto>> GetCommerceTransactionsAsync(CancellationToken cancellationToken = default)
    {
        var orders = await orderRepository.GetCommerceTransactionsAsync(cancellationToken);
        return orders.Select(MapCommerceTransaction).ToArray();
    }

    public async Task<bool> DeleteOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await orderRepository.GetByIdAsync(id, cancellationToken);
        if (order is null)
        {
            return false;
        }

        await orderRepository.DeleteAsync(order, cancellationToken);
        return true;
    }

    private static OrderDto Map(Order order) => new(
        order.Id,
        order.OrderNumber,
        order.UserProfileId,
        order.Status,
        order.Subtotal,
        order.Shipping,
        order.Tax,
        order.CouponCode,
        order.CouponDiscountPercent,
        order.DiscountAmount,
        order.Total,
        order.WalletAddress,
        order.PaymentTransactionHash,
        order.PaymentChainId,
        order.PaymentNetworkName,
        order.PaymentEthAmount,
        order.PaymentExplorerUrl,
        order.PaidAtUtc,
        order.CreatedAt,
        order.Items.Select(item => new CartItemDto(
            item.ProductId,
            item.ProductName,
            item.Quantity,
            item.UnitPrice)).ToArray(),
        order.TransparencyRecords.Select(record => new TransparencyRecordDto(
            record.Id,
            record.OrderId,
            record.OrderNumber,
            record.ProductName,
            record.Quantity,
            record.Total,
            record.OrderHash,
            record.ChainId,
            record.NetworkName,
            record.ContractAddress,
            record.TransactionHash,
            record.ExplorerUrl,
            record.Status,
            record.CreatedAt,
            record.RecordedOnChainAt)).ToArray());

    private static CommerceTransactionDto MapCommerceTransaction(Order order) => new(
        order.Id,
        order.OrderNumber,
        order.Status,
        order.Total,
        order.WalletAddress,
        order.PaymentTransactionHash,
        order.PaymentChainId,
        order.PaymentNetworkName,
        order.PaymentEthAmount,
        order.PaymentExplorerUrl,
        order.PaidAtUtc,
        order.CreatedAt,
        order.Items.Sum(item => item.Quantity),
        BuildProductSummary(order.Items));

    private async Task<Coupon?> ResolveCouponAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CouponCode))
        {
            return null;
        }

        var coupon = await couponRepository.GetByNormalizedCodeAsync(
            CouponService.NormalizeCode(request.CouponCode),
            cancellationToken);
        if (coupon is null || !coupon.IsActive)
        {
            throw new InvalidOperationException("Coupon code is invalid or inactive.");
        }

        var pricing = pricingService.Calculate(request.Items);
        if (pricing.TotalBeforeDiscount < coupon.MinimumOrderTotal)
        {
            throw new InvalidOperationException(
                $"Coupon requires a minimum order total of {coupon.MinimumOrderTotal:C}.");
        }

        if (await couponRepository.HasUserRedeemedAsync(coupon.Id, request.UserProfileId, cancellationToken))
        {
            throw new InvalidOperationException("You have already redeemed this coupon.");
        }

        return coupon;
    }

    private static string BuildProductSummary(IReadOnlyCollection<OrderItem> items)
    {
        if (items.Count == 0)
        {
            return "No items";
        }

        var productNames = items
            .OrderBy(item => item.ProductName)
            .Select(item => item.Quantity > 1 ? $"{item.ProductName} x{item.Quantity}" : item.ProductName)
            .ToArray();

        return string.Join(", ", productNames);
    }
}
