using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Infrastructure.Persistence.Repositories;
using ThisCafeteria.Infrastructure.Services;

namespace ThisCafeteria.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured. " +
                "Set it using environment variable ConnectionStrings__DefaultConnection.");
        }

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IS3StorageService, S3StorageService>();
        services.AddScoped<ISqsMessagePublisher, SqsMessagePublisher>();
        services.AddScoped<IEmailSender, SesEmailSender>();
        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
