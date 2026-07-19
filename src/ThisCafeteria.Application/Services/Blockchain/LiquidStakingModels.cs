namespace ThisCafeteria.Application.Services.Blockchain;

public sealed class LiquidStakingDashboard
{
    public string ChainKey { get; init; } = string.Empty;
    public string Family { get; init; } = string.Empty;
    public string WalletIdentifier { get; init; } = string.Empty;
    public bool IsConfigured { get; init; }
    public string? UnavailableReason { get; init; }
    public decimal CafeBalance { get; init; }
    public decimal StCafeBalance { get; init; }
    public decimal RedeemableCafe { get; init; }
    public decimal ExchangeRate { get; init; } = 1m;
    public decimal PendingCoffee { get; init; }
    public decimal CoffeeBalance { get; init; }
    public decimal DepositPreviewShares { get; init; }
    public decimal RedeemPreviewCafe { get; init; }
    public decimal NativeGasBalance { get; init; }
    public string VaultIdentifier { get; init; } = string.Empty;
    public string AssetIdentifier { get; init; } = string.Empty;
    public string ReceiptIdentifier { get; init; } = string.Empty;
    public string RewardIdentifier { get; init; } = string.Empty;
    public DateTimeOffset ReadAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public enum LiquidStakingOperation
{
    Deposit,
    Redeem,
    Claim,
    RewardFunding
}

public sealed record LiquidTransactionVerificationResult(
    bool Verified,
    TransactionVerificationStatus Status,
    decimal AssetAmount = 0m,
    decimal ShareAmount = 0m,
    decimal RewardAmount = 0m,
    long BlockNumber = 0,
    int OperationIndex = 0,
    string? Error = null)
{
    public static LiquidTransactionVerificationResult Failed(string? error = null) => new(false, TransactionVerificationStatus.Failed, Error: error);
}

public interface ILiquidStakingGateway
{
    Task<LiquidStakingDashboard> GetDashboardAsync(string chainKey, string walletIdentifier, CancellationToken cancellationToken = default);
    Task<LiquidTransactionVerificationResult> VerifyAsync(string chainKey, string walletIdentifier, string transactionId, LiquidStakingOperation operation, decimal? expectedAmount, CancellationToken cancellationToken = default);
}
