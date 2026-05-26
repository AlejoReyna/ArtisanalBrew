namespace ThisCafeteria.Domain.Entities;

public sealed class RewardClaim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string WalletAddress { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string ClaimType { get; set; } = "daily";
    public string? TransactionHash { get; set; }
    public string? PaymentTransactionHash { get; set; }
    public decimal? PaymentAmount { get; set; }
    public int? PaymentChainId { get; set; }
    public string? PaymentNetworkName { get; set; }
    public string? PaymentTokenContract { get; set; }
    public string? MarketplaceWallet { get; set; }
    public string? AllocationName { get; set; }
    public string? PaymentExplorerUrl { get; set; }
    public string? MintExplorerUrl { get; set; }
    public DateTime ClaimedAtUtc { get; set; } = DateTime.UtcNow;
}
