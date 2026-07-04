namespace ThisCafeteria.Domain.Entities;

public sealed class StakingReconciliationCheckpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StakingPoolContract { get; set; } = string.Empty;
    public long LastScannedBlock { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
