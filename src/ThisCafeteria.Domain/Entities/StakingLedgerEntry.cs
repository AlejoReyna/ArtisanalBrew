namespace ThisCafeteria.Domain.Entities;

public sealed class StakingLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string WalletAddress { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string TransactionHash { get; set; } = string.Empty;
    public int ChainId { get; set; }
    public string NetworkName { get; set; } = string.Empty;
    public string PaymentTokenContract { get; set; } = string.Empty;
    public string StakingPoolContract { get; set; } = string.Empty;
    public string ExplorerUrl { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}
