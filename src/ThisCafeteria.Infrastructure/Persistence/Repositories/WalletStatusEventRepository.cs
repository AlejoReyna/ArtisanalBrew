using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Repositories;

public sealed class WalletStatusEventRepository(AppDbContext dbContext) : IWalletStatusEventRepository
{
    public async Task AddAsync(WalletStatusEvent statusEvent, CancellationToken cancellationToken = default)
    {
        dbContext.WalletStatusEvents.Add(statusEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkPublishedToAwsAsync(
        Guid id,
        string awsMessageId,
        DateTimeOffset publishedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var statusEvent = await dbContext.WalletStatusEvents
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (statusEvent is null)
        {
            return;
        }

        statusEvent.AwsMessageId = awsMessageId;
        statusEvent.PublishedToAwsAtUtc = publishedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<WalletStatusEvent?> GetLatestForWalletAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.WalletStatusEvents
            .AsNoTracking()
            .Where(statusEvent => statusEvent.WalletAddress == walletAddress)
            .OrderByDescending(statusEvent => statusEvent.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<WalletStatusEvent>> GetRecentAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.WalletStatusEvents
            .AsNoTracking()
            .OrderByDescending(statusEvent => statusEvent.CreatedAt)
            .Take(count)
            .ToArrayAsync(cancellationToken);
    }
}
