using FluentValidation;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Domain.Enums;

namespace ThisCafeteria.Application.Services;

public sealed class ProfileService(
    IUserProfileRepository userProfileRepository,
    IValidator<UpdateUserProfileRequest> updateValidator,
    IValidator<UpdateAvatarRequest> avatarValidator) : IProfileService
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
            totalOrders,
            RobotAvatarDto.Resolve(profile.Avatar, applicationUser?.WalletAddress));
    }

    public async Task<RobotAvatarDto> GetAvatarForApplicationUserAsync(
        string applicationUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationUserId))
        {
            return RobotAvatarDto.Resolve(null, null);
        }

        var applicationUser = await userProfileRepository.GetApplicationUserProfileAsync(
            applicationUserId,
            cancellationToken);

        if (applicationUser is null)
        {
            return RobotAvatarDto.Resolve(null, null);
        }

        // No linked profile yet means nothing has been saved, which is exactly
        // the case the wallet seed exists for — no row needs to be created to
        // answer this.
        UserProfile? profile = applicationUser.UserProfileId is { } userProfileId
            ? await userProfileRepository.GetByIdAsync(userProfileId, cancellationToken)
            : null;

        return RobotAvatarDto.Resolve(profile?.Avatar, applicationUser.WalletAddress);
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

        return await DescribeAsync(profile, cancellationToken);
    }

    public async Task<UserProfileDto> UpdateAvatarAsync(
        Guid userProfileId,
        UpdateAvatarRequest request,
        CancellationToken cancellationToken = default)
    {
        await avatarValidator.ValidateAndThrowAsync(request, cancellationToken);

        var profile = await userProfileRepository.GetByIdAsync(userProfileId, cancellationToken)
            ?? throw new InvalidOperationException("The requested profile could not be found.");

        // Stored exactly as chosen. The catalog already vouched for every id
        // above, and normalising on write would erase the user's pick the day
        // an item is renamed rather than the day it is rendered.
        profile.Avatar = request.ToRobotAvatar();

        await userProfileRepository.UpdateAsync(profile, cancellationToken);

        return await DescribeAsync(profile, cancellationToken);
    }

    public async Task<UserProfileDto> ResetAvatarAsync(
        Guid userProfileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await userProfileRepository.GetByIdAsync(userProfileId, cancellationToken)
            ?? throw new InvalidOperationException("The requested profile could not be found.");

        // Null, not the seed's current values: the profile has to go back to
        // *tracking* its seed, not freeze a copy of today's version of it.
        profile.Avatar = null;

        await userProfileRepository.UpdateAsync(profile, cancellationToken);

        return await DescribeAsync(profile, cancellationToken);
    }

    public async Task DeleteAccountAsync(Guid userProfileId, CancellationToken cancellationToken = default)
    {
        await userProfileRepository.DeleteProfileCascadeAsync(userProfileId, cancellationToken);
    }

    /// <summary>
    /// Builds the profile DTO, resolving the avatar against the wallet the
    /// account is linked to.
    /// </summary>
    private async Task<UserProfileDto> DescribeAsync(
        UserProfile profile,
        CancellationToken cancellationToken)
    {
        var applicationUser = await userProfileRepository.GetApplicationUserProfileByProfileIdAsync(
            profile.Id,
            cancellationToken);

        return new UserProfileDto(
            profile.Id,
            profile.DisplayName,
            profile.Email,
            applicationUser?.WalletAddress,
            applicationUser?.WalletChainId,
            profile.CreatedAt,
            profile.Role.ToString(),
            RobotAvatarDto.Resolve(profile.Avatar, applicationUser?.WalletAddress));
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
