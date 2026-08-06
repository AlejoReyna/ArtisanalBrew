using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Application.Services.Rewards;

namespace ThisCafeteria.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, bool hasDatabase)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<IOrderPricingService, OrderPricingService>();

        // The services below all depend (via their repositories) on AppDbContext, which is only
        // registered when hasDatabase - see ThisCafeteria.Infrastructure.DependencyInjection.
        if (hasDatabase)
        {
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<ICouponService, CouponService>();
            services.AddScoped<ITransparencyService, TransparencyService>();
            services.AddScoped<IProfileService, ProfileService>();
            services.AddScoped<ILiquidStakingLedgerService, LiquidStakingLedgerService>();
            services.AddScoped<IStakingLedgerService, StakingLedgerService>();
            services.AddScoped<ILoyaltyMintService, LoyaltyMintService>();
        }

        return services;
    }
}
