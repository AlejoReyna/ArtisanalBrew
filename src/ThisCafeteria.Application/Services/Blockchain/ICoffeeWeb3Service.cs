namespace ThisCafeteria.Application.Services.Blockchain;

public interface ICoffeeWeb3Service
{
    Task<CoffeeDashboardModel> GetDashboardDataAsync(string walletAddress, CancellationToken cancellationToken = default);

    Task<decimal> GetCoffeeCoinBalanceAsync(string walletAddress, CancellationToken cancellationToken = default);

    Task<decimal> GetTotalCoffeeSupplyAsync(CancellationToken cancellationToken = default);

    /// <summary>Mints COFFEE to <paramref name="toAddress"/> using the configured owner account. Returns the transaction hash.</summary>
    Task<string> MintCoffeeCoinAsync(string toAddress, decimal amount, CancellationToken cancellationToken = default);

    Task<TransactionVerificationStatus> VerifyPaymentTransactionAsync(
        string txHash,
        string expectedCustomer,
        decimal expectedAmount,
        CancellationToken cancellationToken = default);

    /// <summary>Verifies a native ETH transfer (checkout payment) to the marketplace wallet, tracking block confirmations.</summary>
    Task<StakingVerificationResult> VerifyNativeEthPaymentAsync(
        string txHash,
        string expectedWallet,
        decimal expectedAmountEth,
        CancellationToken cancellationToken = default);

    Task<decimal> GetStakedPaymentTokenBalanceAsync(
        string walletAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Returns null when the on-chain read fails (e.g. RPC outage) rather than a false zero.</summary>
    Task<decimal?> GetPendingStakingRewardsAsync(
        string walletAddress,
        CancellationToken cancellationToken = default);

    Task<StakingVerificationResult> VerifyStakingTransactionAsync(
        string txHash,
        string expectedWallet,
        decimal? expectedAmount,
        StakingTransactionType transactionType,
        CancellationToken cancellationToken = default);

    /// <summary>Reads live claim amount, cooldown, and eligibility from the configured CafeFaucet contract.</summary>
    Task<CafeFaucetStatus> GetCafeFaucetStatusAsync(
        string walletAddress,
        CancellationToken cancellationToken = default);

    bool IsMintingConfigured { get; }
}
