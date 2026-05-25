namespace ThisCafeteria.Application.Services.Blockchain;

public interface ICoffeeWeb3Service
{
    Task<CoffeeDashboardModel> GetDashboardDataAsync(string walletAddress, CancellationToken cancellationToken = default);

    Task<decimal> GetCoffeeCoinBalanceAsync(string walletAddress, CancellationToken cancellationToken = default);

    Task<decimal> GetTotalCoffeeSupplyAsync(CancellationToken cancellationToken = default);

    /// <summary>Mints COFFEE to <paramref name="toAddress"/> using the configured owner account. Returns the transaction hash.</summary>
    Task<string> MintCoffeeCoinAsync(string toAddress, decimal amount, CancellationToken cancellationToken = default);

    bool IsMintingConfigured { get; }
}
