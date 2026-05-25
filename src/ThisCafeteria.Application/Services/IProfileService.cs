using ThisCafeteria.Application.DTOs;

namespace ThisCafeteria.Application.Services;

public interface IProfileService
{
    Task<Guid> EnsureProfileLinkedAsync(string applicationUserId, CancellationToken cancellationToken = default);
    Task<ProfileDashboardDto> GetProfileDashboardAsync(Guid userProfileId, CancellationToken cancellationToken = default);
    Task<UserProfileDto> UpdateDisplayNameAsync(
        Guid userProfileId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken = default);
}
