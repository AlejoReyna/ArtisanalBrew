using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services;

public interface ITransparencyService
{
    Task CreatePendingRecordsForOrderAsync(Order order, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<TransparencyRecordDto>> GetRecentPurchasesAsync(int count = 25, CancellationToken cancellationToken = default);
}
