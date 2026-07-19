using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.Web.Services.Blockchain;

// Per-circuit cache of the wallet dashboard snapshot so lightweight consumers
// (e.g. the nav drawer stat tiles) can show real staked/reward figures without
// issuing an RPC batch on every page render.
public sealed class WalletDashboardState(
    ICoffeeWeb3Service web3Service,
    IChainRegistry registry,
    ILiquidStakingGateway liquidStakingGateway)
{
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);

    public CoffeeDashboardModel? Snapshot { get; private set; }

    public string? WalletAddress { get; private set; }
    public string ChainKey { get; private set; } = "ethereum-sepolia";
    public string Family { get; private set; } = "Evm";

    public DateTimeOffset FetchedAt { get; private set; }

    public event Action? Changed;

    public void Publish(string walletAddress, CoffeeDashboardModel snapshot)
        => Publish(ChainKey, Family, walletAddress, snapshot);

    public void Publish(string chainKey, string family, string walletAddress, CoffeeDashboardModel snapshot)
    {
        ChainKey = chainKey;
        Family = family;
        WalletAddress = walletAddress;
        Snapshot = snapshot;
        FetchedAt = DateTimeOffset.UtcNow;
        Changed?.Invoke();
    }

    public async Task<CoffeeDashboardModel?> GetOrRefreshAsync(
        string walletAddress,
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
        => await GetOrRefreshAsync(ChainKey, Family, walletAddress, maxAge, cancellationToken);

    public async Task<CoffeeDashboardModel?> GetOrRefreshAsync(
        string chainKey,
        string family,
        string walletAddress,
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        if (IsFresh(chainKey, family, walletAddress, maxAge))
        {
            return Snapshot;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsFresh(chainKey, family, walletAddress, maxAge))
            {
                return Snapshot;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(FetchTimeout);

            var snapshot = await GetChainDashboardAsync(chainKey, family, walletAddress, timeout.Token);
            if (snapshot is null) return null;
            Publish(chainKey, family, walletAddress, snapshot);
            return snapshot;
        }
        catch (Exception)
        {
            // RPC hiccups shouldn't break nav rendering; callers treat null as "unknown".
            return IsFresh(chainKey, family, walletAddress, TimeSpan.MaxValue)
                ? Snapshot
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CoffeeDashboardModel?> GetChainDashboardAsync(string chainKey, string family, string walletAddress, CancellationToken cancellationToken)
    {
        if (string.Equals(chainKey, "ethereum-sepolia", StringComparison.Ordinal))
            return await web3Service.GetDashboardDataAsync(walletAddress, cancellationToken);

        if (!registry.TryGet(chainKey, out var chain) || chain.FamilyName != family || !chain.Capabilities.LiquidStaking)
            return null;

        var dashboard = await liquidStakingGateway.GetDashboardAsync(chainKey, walletAddress, cancellationToken);
        if (!dashboard.IsConfigured) return null;
        return new CoffeeDashboardModel
        {
            WalletAddress = walletAddress,
            NativeBalance = dashboard.NativeGasBalance,
            PaymentTokenBalance = dashboard.CafeBalance,
            StakedPaymentTokenBalance = dashboard.StCafeBalance,
            PendingStakingRewards = dashboard.PendingCoffee,
            CoffeeCoinBalance = dashboard.CoffeeBalance,
            CurrentApr = 0m,
            AprIsContractDerived = false,
            ClaimFunctionName = "claimRewards"
        };
    }

    private bool IsFresh(string chainKey, string family, string walletAddress, TimeSpan maxAge) =>
        Snapshot is not null &&
        string.Equals(ChainKey, chainKey, StringComparison.Ordinal) &&
        string.Equals(Family, family, StringComparison.Ordinal) &&
        string.Equals(WalletAddress, walletAddress, family == ChainFamily.Solana.ToString() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) &&
        DateTimeOffset.UtcNow - FetchedAt < maxAge;
}
