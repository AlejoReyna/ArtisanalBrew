using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Application.DTOs;

public sealed record CommerceTransactionDto(
    string OrderNumber,
    OrderStatus Status,
    decimal Total,
    string WalletAddress,
    string? PaymentTransactionHash,
    int? PaymentChainId,
    string? PaymentNetworkName,
    decimal? PaymentEthAmount,
    string? PaymentExplorerUrl,
    DateTime? PaidAtUtc,
    DateTime CreatedAt,
    int ItemCount,
    string ProductSummary);
