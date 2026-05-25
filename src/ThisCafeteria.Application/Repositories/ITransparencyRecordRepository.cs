using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Repositories;

public interface ITransparencyRecordRepository
{
    Task AddRangeAsync(IReadOnlyCollection<TransparencyRecord> records, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<TransparencyRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default);
}
