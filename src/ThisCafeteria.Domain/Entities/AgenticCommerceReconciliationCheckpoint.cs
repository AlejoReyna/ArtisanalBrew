namespace ThisCafeteria.Domain.Entities;

/// <summary>
/// Tracks the block cursor for the AgenticCommerceEscrow reconciliation worker.
/// One row per (ChainKey, EscrowAddress) pair. The cursor advances only inside
/// the same database transaction as the projections it produces.
/// </summary>
public sealed class AgenticCommerceReconciliationCheckpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ChainKey { get; set; } = string.Empty;
    public string EscrowAddress { get; set; } = string.Empty;
    public long LastScannedBlock { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
