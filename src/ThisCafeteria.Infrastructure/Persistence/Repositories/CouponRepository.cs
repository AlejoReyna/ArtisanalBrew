using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Repositories;

public sealed class CouponRepository(AppDbContext dbContext) : ICouponRepository
{
    public async Task<IReadOnlyCollection<Coupon>> GetCouponsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Coupons
            .AsNoTracking()
            .OrderBy(coupon => coupon.Code)
            .ToArrayAsync(cancellationToken);
    }

    public Task<Coupon?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.Coupons.FirstOrDefaultAsync(coupon => coupon.Id == id, cancellationToken);
    }

    public Task<Coupon?> GetByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken = default)
    {
        return dbContext.Coupons
            .AsNoTracking()
            .FirstOrDefaultAsync(coupon => coupon.NormalizedCode == normalizedCode, cancellationToken);
    }

    public Task<bool> HasUserRedeemedAsync(Guid couponId, Guid userProfileId, CancellationToken cancellationToken = default)
    {
        return dbContext.CouponRedemptions.AnyAsync(
            redemption => redemption.CouponId == couponId && redemption.UserProfileId == userProfileId,
            cancellationToken);
    }

    public async Task AddAsync(Coupon coupon, CancellationToken cancellationToken = default)
    {
        dbContext.Coupons.Add(coupon);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Coupon coupon, CancellationToken cancellationToken = default)
    {
        dbContext.Coupons.Update(coupon);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Coupon coupon, CancellationToken cancellationToken = default)
    {
        var hasRedemptions = await dbContext.CouponRedemptions
            .AnyAsync(redemption => redemption.CouponId == coupon.Id, cancellationToken);
        if (hasRedemptions)
        {
            coupon.IsActive = false;
            coupon.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            dbContext.Coupons.Remove(coupon);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
