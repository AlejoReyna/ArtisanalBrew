using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Repositories;

public interface IWalletStatusEventRepository
{
    Task AddAsync(WalletStatusEvent statusEvent, CancellationToken cancellationToken = default);
    Task MarkPublishedToAwsAsync(Guid id, string awsMessageId, DateTimeOffset publishedAtUtc, CancellationToken cancellationToken = default);
    Task<WalletStatusEvent?> GetLatestForWalletAsync(string walletAddress, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<WalletStatusEvent>> GetRecentAsync(int count, CancellationToken cancellationToken = default);
}
