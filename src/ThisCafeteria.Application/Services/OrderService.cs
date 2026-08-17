using FluentValidation;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services;

public sealed class OrderService(
    IOrderRepository orderRepository,
    IProductRepository productRepository,
    ICouponRepository couponRepository,
    IOrderPricingService pricingService,
    IMarketplacePaymentGateway paymentGateway,
    IChainRegistry chainRegistry,
    ITransparencyService transparencyService,
    IValidator<CreateOrderRequest> validator) : IOrderService
{
    public async Task<OrderDto> CreateOrderAsync(
        CreateOrderRequest request,
        Guid authenticatedUserProfileId,
        CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        if (authenticatedUserProfileId == Guid.Empty)
        {
            throw new InvalidOperationException("Sign in before placing an order.");
        }

        var chain = ResolveMarketplaceChain(request.PaymentChainId);
        var catalogItems = await RequoteFromCatalogAsync(request.Items, cancellationToken);
        var coupon = await ResolveCouponAsync(catalogItems, request.CouponCode, authenticatedUserProfileId, cancellationToken);
        var pricing = pricingService.Calculate(catalogItems, coupon);
        var expectedNativeAmount = pricingService.ToNativePaymentAmount(pricing.Total, chain.NativeCurrencySymbol);

        if (await orderRepository.ExistsByPaymentHashAsync(request.PaymentTransactionHash, cancellationToken))
        {
            throw new InvalidOperationException("This payment has already been used for an order.");
        }

        var verification = await paymentGateway.VerifyNativePaymentAsync(
            chain.Key,
            request.PaymentTransactionHash,
            request.WalletAddress,
            expectedNativeAmount,
            cancellationToken);

        if (verification.Status == TransactionVerificationStatus.PendingConfirmations)
        {
            throw new InvalidOperationException("This payment is still confirming. Try again in a few seconds.");
        }

        if (verification.Status != TransactionVerificationStatus.Verified)
        {
            throw new InvalidOperationException("This payment could not be verified on-chain.");
        }

        await DecrementStockAsync(catalogItems, cancellationToken);

        var items = catalogItems.Select(item => new OrderItem
        {
            ProductId = item.ProductId,
            ProductName = item.ProductName,
            Quantity = item.Quantity,
            UnitPrice = item.UnitPrice,
            Total = item.UnitPrice * item.Quantity
        }).ToList();
        var order = Order.Place(
            authenticatedUserProfileId,
            $"TC-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}",
            request.WalletAddress,
            items,
            new OrderPricing(
                pricing.Subtotal,
                pricing.Shipping,
                pricing.Tax,
                pricing.TotalBeforeDiscount,
                pricing.DiscountAmount,
                pricing.Total),
            coupon);
        order.RecordPayment(
            request.PaymentTransactionHash,
            chain.EvmChainId,
            chain.DisplayName,
            expectedNativeAmount,
            chain.ExplorerTransactionTemplate.Replace("{0}", request.PaymentTransactionHash, StringComparison.Ordinal),
            DateTime.UtcNow);

        if (coupon is not null)
        {
            order.CouponRedemption = new CouponRedemption
            {
                CouponId = coupon.Id,
                UserProfileId = authenticatedUserProfileId,
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

    private ChainDefinition ResolveMarketplaceChain(int paymentChainId)
    {
        var chain = chainRegistry.All.SingleOrDefault(candidate =>
            candidate.Enabled &&
            candidate.Family == ChainFamily.Evm &&
            candidate.EvmChainId == paymentChainId);

        if (chain is null || !chain.Capabilities.MarketplacePayment)
        {
            throw new InvalidOperationException("Checkout is not available on the selected network.");
        }

        return chain;
    }

    private async Task<IReadOnlyCollection<CartItemDto>> RequoteFromCatalogAsync(
        IReadOnlyCollection<CartItemDto> requestedItems,
        CancellationToken cancellationToken)
    {
        var quoted = new List<CartItemDto>(requestedItems.Count);
        foreach (var requested in requestedItems)
        {
            var product = await productRepository.GetProductByIdAsync(requested.ProductId, cancellationToken);
            if (product is null || !product.IsActive)
            {
                throw new InvalidOperationException($"Product '{requested.ProductName}' is no longer available.");
            }

            if (product.StockQuantity < requested.Quantity)
            {
                throw new InvalidOperationException(
                    $"Only {product.StockQuantity} unit(s) of '{product.Name}' are available.");
            }

            quoted.Add(new CartItemDto(product.Id, product.Name, requested.Quantity, product.Price));
        }

        return quoted;
    }

    private async Task DecrementStockAsync(
        IReadOnlyCollection<CartItemDto> items,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            var product = await productRepository.GetProductByIdAsync(item.ProductId, cancellationToken);
            if (product is null || product.StockQuantity < item.Quantity)
            {
                throw new InvalidOperationException($"'{item.ProductName}' went out of stock before the order could be recorded.");
            }

            product.StockQuantity -= item.Quantity;
            product.UpdatedAt = DateTime.UtcNow;
            await productRepository.UpdateAsync(product, cancellationToken);
        }
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

    private async Task<Coupon?> ResolveCouponAsync(
        IReadOnlyCollection<CartItemDto> items,
        string? couponCode,
        Guid userProfileId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(couponCode))
        {
            return null;
        }

        var coupon = await couponRepository.GetByNormalizedCodeAsync(
            CouponService.NormalizeCode(couponCode),
            cancellationToken);
        if (coupon is null || !coupon.IsActive)
        {
            throw new InvalidOperationException("Coupon code is invalid or inactive.");
        }

        var pricing = pricingService.Calculate(items);
        if (!coupon.CanBeRedeemedFor(pricing.TotalBeforeDiscount))
        {
            throw new InvalidOperationException(
                $"Coupon requires a minimum order total of {coupon.MinimumOrderTotal:C}.");
        }

        if (await couponRepository.HasUserRedeemedAsync(coupon.Id, userProfileId, cancellationToken))
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
