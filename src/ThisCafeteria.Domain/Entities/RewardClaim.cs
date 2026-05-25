namespace ThisCafeteria.Domain.Entities;

public sealed class RewardClaim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string WalletAddress { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string ClaimType { get; set; } = "daily";
    public string? TransactionHash { get; set; }
    public string? PaymentTransactionHash { get; set; }
    public DateTime ClaimedAtUtc { get; set; } = DateTime.UtcNow;
}
