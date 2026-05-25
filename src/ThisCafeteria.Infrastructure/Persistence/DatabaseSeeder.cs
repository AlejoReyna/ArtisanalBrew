using Microsoft.EntityFrameworkCore;

namespace ThisCafeteria.Infrastructure.Persistence;

public sealed class DatabaseSeeder(AppDbContext dbContext)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
