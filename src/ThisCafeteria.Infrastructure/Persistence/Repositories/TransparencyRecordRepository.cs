using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Repositories;

public sealed class TransparencyRecordRepository(AppDbContext dbContext) : ITransparencyRecordRepository
{
    public async Task AddRangeAsync(IReadOnlyCollection<TransparencyRecord> records, CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return;
        }

        dbContext.TransparencyRecords.AddRange(records);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<TransparencyRecord>> GetRecentAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.TransparencyRecords
            .AsNoTracking()
            .OrderByDescending(record => record.CreatedAt)
            .Take(count)
            .ToArrayAsync(cancellationToken);
    }
}
