using ThisCafeteria.Application.DTOs;

namespace ThisCafeteria.Application.Services;

public interface IProfileService
{
    Task<Guid> EnsureProfileLinkedAsync(string applicationUserId, CancellationToken cancellationToken = default);
    Task<ProfileDashboardDto> GetProfileDashboardAsync(Guid userProfileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The account's robot, for callers that only need the avatar.
    /// </summary>
    /// <remarks>
    /// Read-only on purpose. <see cref="EnsureProfileLinkedAsync"/> would also
    /// get here but it <em>creates</em> a profile and cart as a side effect,
    /// which has no business happening because someone rendered a header. An
    /// account with no linked profile yet still gets a robot — seeded from its
    /// wallet, which is all the seed ever needed.
    /// </remarks>
    Task<RobotAvatarDto> GetAvatarForApplicationUserAsync(
        string applicationUserId,
        CancellationToken cancellationToken = default);
    Task<UserProfileDto> UpdateDisplayNameAsync(
        Guid userProfileId,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken = default);

    Task<UserProfileDto> UpdateAvatarAsync(
        Guid userProfileId,
        UpdateAvatarRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the saved look so the profile goes back to rendering the robot
    /// derived from its wallet address.
    /// </summary>
    /// <remarks>
    /// Distinct from saving the seed's current values: this restores the
    /// "never edited" state, so the avatar keeps tracking the seed rather than
    /// freezing today's version of it into the column.
    /// </remarks>
    Task<UserProfileDto> ResetAvatarAsync(Guid userProfileId, CancellationToken cancellationToken = default);

    Task DeleteAccountAsync(Guid userProfileId, CancellationToken cancellationToken = default);
}
