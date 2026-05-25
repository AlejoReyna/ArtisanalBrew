namespace ThisCafeteria.Application.DTOs;

public sealed record ProfileDashboardDto(
    Guid UserProfileId,
    string DisplayName,
    string Email,
    string? WalletAddress,
    int? WalletChainId,
    DateTime CreatedAt,
    string Role,
    int TotalOrders);
