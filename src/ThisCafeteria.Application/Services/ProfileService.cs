using FluentValidation;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Application.Services;

public sealed class ProfileService(
    IUserProfileRepository userProfileRepository,
    IValidator<UpdateUserProfileRequest> updateValidator) : IProfileService
{
    public async Task<Guid> EnsureProfileLinkedAsync(
        string applicationUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationUserId))
        {
            throw new ArgumentException("Application user id is required.", nameof(applicationUserId));
        }

        var applicationUser = await userProfileRepository.GetApplicationUserProfileAsync(
            applicationUserId,
            cancellationToken);

        if (applicationUser is null)
        {
            throw new InvalidOperationException("The authenticated user could not be found.");
        }

        if (applicationUser.UserProfileId is { } existingProfileId)
        {
            return existingProfileId;
        }

        var walletAddress = applicationUser.WalletAddress ?? applicationUser.UserName ?? applicationUserId;
        var profile = new UserProfile
        {
            Email = CreateSyntheticEmail(walletAddress),
            DisplayName = CreateDefaultDisplayName(walletAddress),
            Role = UserRole.Customer,
            CreatedAt = DateTime.UtcNow
        };
        var cart = new Cart
        {
            UserProfileId = profile.Id,
            CreatedAt = DateTime.UtcNow
        };

        return await userProfileRepository.CreateProfileAndCartForApplicationUserAsync(
            applicationUserId,
            profile,
            cart,
            cancellationToken);
    }

    public async Task<ProfileDashboardDto> GetProfileDashboardAsync(
        Guid userProfileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await userProfileRepository.GetByIdAsync(userProfileId, cancellationToken)
            ?? throw new InvalidOperationException("The requested profile could not be found.");
        var applicationUser = await userProfileRepository.GetApplicationUserProfileByProfileIdAsync(
            userProfileId,
            cancellationToken);
        var totalOrders = await userProfileRepository.CountOrdersAsync(userProfileId, cancellationToken);

        return new ProfileDashboardDto(
            profile.Id,
            profile.DisplayName,
            profile.Email,
            applicationUser?.WalletAddress,
            applicationUser?.WalletChainId,
            profile.CreatedAt,
            profile.Role.ToString(),
            totalOrders);
    }

    public async Task<UserProfileDto> UpdateDisplayNameAsync(
        Guid userProfileId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedRequest = request with { DisplayName = request.DisplayName.Trim() };
        await updateValidator.ValidateAndThrowAsync(normalizedRequest, cancellationToken);

        var profile = await userProfileRepository.GetByIdAsync(userProfileId, cancellationToken)
            ?? throw new InvalidOperationException("The requested profile could not be found.");
        profile.DisplayName = normalizedRequest.DisplayName;

        await userProfileRepository.UpdateAsync(profile, cancellationToken);

        var applicationUser = await userProfileRepository.GetApplicationUserProfileByProfileIdAsync(
            userProfileId,
            cancellationToken);

        return new UserProfileDto(
            profile.Id,
            profile.DisplayName,
            profile.Email,
            applicationUser?.WalletAddress,
            applicationUser?.WalletChainId,
            profile.CreatedAt,
            profile.Role.ToString());
    }

    private static string CreateSyntheticEmail(string walletAddress)
    {
        var localPart = walletAddress.Trim().ToLowerInvariant();
        return $"{localPart}@wallet.thiscafeteria.local";
    }

    private static string CreateDefaultDisplayName(string walletAddress)
    {
        var trimmed = walletAddress.Trim();
        return trimmed.Length >= 12
            ? $"{trimmed[..6]}...{trimmed[^4..]}"
            : trimmed;
    }
}
