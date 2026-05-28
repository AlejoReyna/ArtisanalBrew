namespace ThisCafeteria.Application.DTOs;

public sealed record CreateOrderRequest(
    Guid UserProfileId,
    IReadOnlyCollection<CartItemDto> Items,
    string WalletAddress,
    string PaymentTransactionHash,
    int PaymentChainId,
    string PaymentNetworkName,
    decimal PaymentEthAmount,
    string PaymentExplorerUrl,
    DateTime PaidAtUtc,
    string? CouponCode = null);
