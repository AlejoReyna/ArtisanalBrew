namespace ThisCafeteria.Domain.Entities;

public sealed class StakingReconciliationCheckpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ChainKey { get; set; } = "ethereum-sepolia";
    public string Family { get; set; } = "Evm";
    public string SourceIdentifier { get; set; } = string.Empty;
    public string CursorType { get; set; } = "block";
    public string StakingPoolContract { get; set; } = string.Empty;
    public long LastScannedBlock { get; set; }
    public long LastScannedSlot { get; set; }
    public string LastScannedSignature { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
