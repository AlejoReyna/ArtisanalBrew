using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ThisCafeteria.Application.Services;

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
        }

        return services;
    }
}
