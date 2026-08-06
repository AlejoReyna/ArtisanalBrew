using FluentValidation;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services;

public sealed class CouponService(
    ICouponRepository couponRepository,
    IOrderPricingService pricingService,
    IValidator<CreateCouponRequest> createValidator,
    IValidator<UpdateCouponRequest> updateValidator) : ICouponService
{
    public async Task<IReadOnlyCollection<CouponDto>> GetCouponsAsync(CancellationToken cancellationToken = default)
    {
        var coupons = await couponRepository.GetCouponsAsync(cancellationToken);
        return coupons.Select(Map).ToArray();
    }

    public async Task<CouponDto> CreateCouponAsync(CreateCouponRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var normalizedCode = Coupon.NormalizeCode(request.Code);
        if (await couponRepository.GetByNormalizedCodeAsync(normalizedCode, cancellationToken) is not null)
        {
            throw new InvalidOperationException($"Coupon '{request.Code.Trim()}' already exists.");
        }

        var coupon = Coupon.Create(request.Code, request.DiscountPercent, request.MinimumOrderTotal);

        await couponRepository.AddAsync(coupon, cancellationToken);
        return Map(coupon);
    }

    public async Task<CouponDto?> UpdateCouponAsync(Guid id, UpdateCouponRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var coupon = await couponRepository.GetByIdAsync(id, cancellationToken);
        if (coupon is null)
        {
            return null;
        }

        var normalizedCode = Coupon.NormalizeCode(request.Code);
        var existing = await couponRepository.GetByNormalizedCodeAsync(normalizedCode, cancellationToken);
        if (existing is not null && existing.Id != id)
        {
            throw new InvalidOperationException($"Coupon '{request.Code.Trim()}' already exists.");
        }

        coupon.Update(
            request.Code,
            request.DiscountPercent,
            request.MinimumOrderTotal,
            request.IsActive,
            DateTime.UtcNow);

        await couponRepository.UpdateAsync(coupon, cancellationToken);
        return Map(coupon);
    }

    public async Task<bool> DeleteCouponAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var coupon = await couponRepository.GetByIdAsync(id, cancellationToken);
        if (coupon is null)
        {
            return false;
        }

        await couponRepository.DeleteAsync(coupon, cancellationToken);
        return true;
    }

    public async Task<CouponQuoteDto> QuoteCouponAsync(
        Guid userProfileId,
        IReadOnlyCollection<CartItemDto> items,
        string code,
        CancellationToken cancellationToken = default)
    {
        var coupon = await ResolveRedeemableCouponAsync(userProfileId, items, code, cancellationToken);
        return new CouponQuoteDto(Map(coupon), pricingService.Calculate(items, coupon));
    }

    internal async Task<Coupon> ResolveRedeemableCouponAsync(
        Guid userProfileId,
        IReadOnlyCollection<CartItemDto> items,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (userProfileId == Guid.Empty)
        {
            throw new InvalidOperationException("Sign in before applying a coupon.");
        }

        if (items.Count == 0)
        {
            throw new InvalidOperationException("Add items before applying a coupon.");
        }

        var coupon = await couponRepository.GetByNormalizedCodeAsync(Coupon.NormalizeCode(code), cancellationToken);
        if (coupon is null || !coupon.IsActive)
        {
            throw new InvalidOperationException("Coupon code is invalid or inactive.");
        }

        var pricing = pricingService.Calculate(items);
        if (!coupon.CanBeRedeemedFor(pricing.TotalBeforeDiscount))
        {
            throw new InvalidOperationException(
                $"Coupon requires a minimum order total of {coupon.MinimumOrderTotal:C}.");
        }

        if (await couponRepository.HasUserRedeemedAsync(coupon.Id, userProfileId, cancellationToken))
        {
            throw new InvalidOperationException("You have already redeemed this coupon.");
        }

        return coupon;
    }

    public static string NormalizeCode(string code) => Coupon.NormalizeCode(code);

    private static CouponDto Map(Coupon coupon) => new(
        coupon.Id,
        coupon.Code,
        coupon.DiscountPercent,
        coupon.MinimumOrderTotal,
        coupon.IsActive,
        coupon.CreatedAt,
        coupon.UpdatedAt);
}
