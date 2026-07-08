using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Repositories;

public sealed class UserProfileRepository(AppDbContext dbContext) : IUserProfileRepository
{
    public async Task<ApplicationUserProfileSnapshot?> GetApplicationUserProfileAsync(
        string applicationUserId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == applicationUserId)
            .Select(user => new ApplicationUserProfileSnapshot(
                user.Id,
                user.UserProfileId,
                user.Email,
                user.UserName,
                user.WalletAddress,
                user.WalletChainId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ApplicationUserProfileSnapshot?> GetApplicationUserProfileByProfileIdAsync(
        Guid userProfileId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .AsNoTracking()
            .Where(user => user.UserProfileId == userProfileId)
            .Select(user => new ApplicationUserProfileSnapshot(
                user.Id,
                user.UserProfileId,
                user.Email,
                user.UserName,
                user.WalletAddress,
                user.WalletChainId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<UserProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.UserProfiles
            .FirstOrDefaultAsync(profile => profile.Id == id, cancellationToken);
    }

    public async Task<Guid> CreateProfileAndCartForApplicationUserAsync(
        string applicationUserId,
        UserProfile profile,
        Cart cart,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var user = await dbContext.Users
            .FirstOrDefaultAsync(user => user.Id == applicationUserId, cancellationToken)
            ?? throw new InvalidOperationException("The authenticated user could not be found.");

        if (user.UserProfileId is { } existingProfileId)
        {
            await transaction.CommitAsync(cancellationToken);
            return existingProfileId;
        }

        cart.UserProfileId = profile.Id;
        dbContext.UserProfiles.Add(profile);
        dbContext.Carts.Add(cart);
        user.UserProfileId = profile.Id;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return profile.Id;
    }

    public async Task UpdateAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        dbContext.UserProfiles.Update(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> CountOrdersAsync(Guid userProfileId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Orders
            .AsNoTracking()
            .CountAsync(order => order.UserProfileId == userProfileId, cancellationToken);
    }

    public async Task DeleteProfileCascadeAsync(Guid userProfileId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Order -> UserProfile is DeleteBehavior.Restrict (financial records shouldn't vanish via an
        // unrelated cascade), so orders must be deleted explicitly first. That delete does cascade to
        // OrderItems, Receipt, TransparencyRecords and CouponRedemption, which are all keyed off Order.
        await dbContext.Orders
            .Where(order => order.UserProfileId == userProfileId)
            .ExecuteDeleteAsync(cancellationToken);

        // Cart -> UserProfile is DeleteBehavior.Cascade, so this takes the cart and its items with it.
        await dbContext.UserProfiles
            .Where(profile => profile.Id == userProfileId)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
