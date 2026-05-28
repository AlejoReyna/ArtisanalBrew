using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Repositories;

public interface ICouponRepository
{
    Task<IReadOnlyCollection<Coupon>> GetCouponsAsync(CancellationToken cancellationToken = default);
    Task<Coupon?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Coupon?> GetByNormalizedCodeAsync(string normalizedCode, CancellationToken cancellationToken = default);
    Task<bool> HasUserRedeemedAsync(Guid couponId, Guid userProfileId, CancellationToken cancellationToken = default);
    Task AddAsync(Coupon coupon, CancellationToken cancellationToken = default);
    Task UpdateAsync(Coupon coupon, CancellationToken cancellationToken = default);
    Task DeleteAsync(Coupon coupon, CancellationToken cancellationToken = default);
}
