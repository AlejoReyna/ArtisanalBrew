using ThisCafeteria.Application.DTOs;

namespace ThisCafeteria.Application.Services;

public interface ICouponService
{
    Task<IReadOnlyCollection<CouponDto>> GetCouponsAsync(CancellationToken cancellationToken = default);
    Task<CouponDto> CreateCouponAsync(CreateCouponRequest request, CancellationToken cancellationToken = default);
    Task<CouponDto?> UpdateCouponAsync(Guid id, UpdateCouponRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteCouponAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CouponQuoteDto> QuoteCouponAsync(
        Guid userProfileId,
        IReadOnlyCollection<CartItemDto> items,
        string code,
        CancellationToken cancellationToken = default);
}
