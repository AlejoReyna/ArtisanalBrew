using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.Web.Services.Blockchain;

// Per-circuit cache of the wallet dashboard snapshot so lightweight consumers
// (e.g. the nav drawer stat tiles) can show real staked/reward figures without
// issuing an RPC batch on every page render.
public sealed class WalletDashboardState(ICoffeeWeb3Service web3Service)
{
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);

    public CoffeeDashboardModel? Snapshot { get; private set; }

    public string? WalletAddress { get; private set; }

    public DateTimeOffset FetchedAt { get; private set; }

    public event Action? Changed;

    public void Publish(string walletAddress, CoffeeDashboardModel snapshot)
    {
        WalletAddress = walletAddress;
        Snapshot = snapshot;
        FetchedAt = DateTimeOffset.UtcNow;
        Changed?.Invoke();
    }

    public async Task<CoffeeDashboardModel?> GetOrRefreshAsync(
        string walletAddress,
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        if (IsFresh(walletAddress, maxAge))
        {
            return Snapshot;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsFresh(walletAddress, maxAge))
            {
                return Snapshot;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(FetchTimeout);

            var snapshot = await web3Service.GetDashboardDataAsync(walletAddress, timeout.Token);
            Publish(walletAddress, snapshot);
            return snapshot;
        }
        catch (Exception)
        {
            // RPC hiccups shouldn't break nav rendering; callers treat null as "unknown".
            return string.Equals(WalletAddress, walletAddress, StringComparison.OrdinalIgnoreCase)
                ? Snapshot
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsFresh(string walletAddress, TimeSpan maxAge) =>
        Snapshot is not null &&
        string.Equals(WalletAddress, walletAddress, StringComparison.OrdinalIgnoreCase) &&
        DateTimeOffset.UtcNow - FetchedAt < maxAge;
}
