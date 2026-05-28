using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Application.DTOs;

public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    Guid UserProfileId,
    OrderStatus Status,
    decimal Subtotal,
    decimal Shipping,
    decimal Tax,
    string? CouponCode,
    decimal? CouponDiscountPercent,
    decimal DiscountAmount,
    decimal Total,
    string WalletAddress,
    string? PaymentTransactionHash,
    int? PaymentChainId,
    string? PaymentNetworkName,
    decimal? PaymentEthAmount,
    string? PaymentExplorerUrl,
    DateTime? PaidAtUtc,
    DateTime CreatedAt,
    IReadOnlyCollection<CartItemDto> Items,
    IReadOnlyCollection<TransparencyRecordDto> TransparencyRecords);
