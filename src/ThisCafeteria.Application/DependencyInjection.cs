using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ITransparencyService, TransparencyService>();
        services.AddScoped<IProfileService, ProfileService>();

        return services;
    }
}
