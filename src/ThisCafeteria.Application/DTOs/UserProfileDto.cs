namespace ThisCafeteria.Application.DTOs;

public sealed record UserProfileDto(
    Guid UserProfileId,
    string DisplayName,
    string Email,
    string? WalletAddress,
    int? WalletChainId,
    DateTime CreatedAt,
    string Role);
