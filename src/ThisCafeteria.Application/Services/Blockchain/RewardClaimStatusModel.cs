namespace ThisCafeteria.Application.Services.Blockchain;

public sealed class RewardClaimStatusModel
{
    public string WalletAddress { get; init; } = string.Empty;
    public decimal PaymentTokenBalance { get; init; }
    public decimal EstimatedDailyReward { get; init; }
    public decimal ClaimableAmount { get; init; }
    public bool CanClaimToday { get; init; }
    public bool MintingEnabled { get; init; }
    public DateTime? LastClaimedAtUtc { get; init; }
}

public sealed class RewardClaimResultModel
{
    public bool Success { get; init; }
    public string? TransactionHash { get; init; }
    public string? PaymentTransactionHash { get; init; }
    public decimal MintedAmount { get; init; }
    public string? Error { get; init; }
}
