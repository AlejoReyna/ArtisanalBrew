using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Domain.Enums;
using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.Infrastructure.Identity;

public static class AdminIdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var email = configuration["Authentication:AdminEmail"];
        var password = configuration["Authentication:AdminPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await dbContext.Database.MigrateAsync();

        foreach (var role in new[] { UserRole.Admin.ToString(), UserRole.Customer.ToString() })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        var admin = await userManager.FindByEmailAsync(email);
        if (admin is null)
        {
            var profile = new UserProfile
            {
                Email = email,
                DisplayName = "This Cafeteria Admin",
                Role = UserRole.Admin,
                CreatedAt = DateTime.UtcNow
            };

            dbContext.UserProfiles.Add(profile);
            await dbContext.SaveChangesAsync();

            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                UserProfileId = profile.Id
            };

            var result = await userManager.CreateAsync(admin, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
            }
        }

        if (!await userManager.IsInRoleAsync(admin, UserRole.Admin.ToString()))
        {
            await userManager.AddToRoleAsync(admin, UserRole.Admin.ToString());
        }
    }
}
