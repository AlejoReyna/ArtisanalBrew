using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Repositories;

public sealed class OrderRepository(AppDbContext dbContext) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Order>> GetOrdersForUserAsync(Guid userProfileId, CancellationToken cancellationToken = default)
    {
        return await dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .Include(order => order.TransparencyRecords)
            .Where(order => order.UserProfileId == userProfileId)
            .OrderByDescending(order => order.CreatedAt)
            .ToArrayAsync(cancellationToken);
    }
}
