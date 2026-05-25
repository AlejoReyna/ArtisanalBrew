using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Repositories;

public interface IUserProfileRepository
{
    Task<ApplicationUserProfileSnapshot?> GetApplicationUserProfileAsync(
        string applicationUserId,
        CancellationToken cancellationToken = default);

    Task<ApplicationUserProfileSnapshot?> GetApplicationUserProfileByProfileIdAsync(
        Guid userProfileId,
        CancellationToken cancellationToken = default);

    Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Guid> CreateProfileAndCartForApplicationUserAsync(
        string applicationUserId,
        UserProfile profile,
        Cart cart,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(UserProfile profile, CancellationToken cancellationToken = default);

    Task<int> CountOrdersAsync(Guid userProfileId, CancellationToken cancellationToken = default);
}

public sealed record ApplicationUserProfileSnapshot(
    string ApplicationUserId,
    Guid? UserProfileId,
    string? Email,
    string? UserName,
    string? WalletAddress,
    int? WalletChainId);
