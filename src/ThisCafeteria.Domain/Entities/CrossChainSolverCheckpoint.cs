namespace ThisCafeteria.Domain.Entities;

/// <summary>
/// Tracks the block cursor for the cross-chain solver's source-chain scan.
/// One row per (SourceChainKey, SourceResolverAddress) pair.
/// </summary>
public sealed class CrossChainSolverCheckpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceChainKey { get; set; } = string.Empty;
    public string SourceResolverAddress { get; set; } = string.Empty;
    public long LastScannedBlock { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
