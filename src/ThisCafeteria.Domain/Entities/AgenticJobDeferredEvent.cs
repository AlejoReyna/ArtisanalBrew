using System.ComponentModel.DataAnnotations;

namespace ThisCafeteria.Domain.Entities;

/// <summary>
/// Records an escrow event that could not be applied because its prerequisite (e.g. the
/// corresponding JobCreated) had not yet been reconciled.  Deferred events are durable –
/// the reconciliation checkpoint does NOT advance past them silently.
///
/// The unique log identity (ChainKey + ContractAddress + TransactionHash + LogIndex) mirrors
/// AgenticJobAppliedEvent so that the same event is never both applied and deferred.
///
/// Re-application is a future concern (Phase 4).  Recording the event here prevents permanent
/// loss when the checkpoint advances and the log range is not re-scanned.
/// </summary>
public class AgenticJobDeferredEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Chain registry key (e.g. "evm-local", "ethereum-sepolia").</summary>
    [MaxLength(64)]
    public string ChainKey { get; set; } = string.Empty;

    /// <summary>Checksummed-or-lowercase escrow contract address.</summary>
    [MaxLength(128)]
    public string ContractAddress { get; set; } = string.Empty;

    /// <summary>On-chain job ID from the escrow contract. May be 0 if the job does not yet exist.</summary>
    public long OnChainJobId { get; set; }

    /// <summary>The event type name (e.g. "JobCompleted").</summary>
    [MaxLength(64)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>Transaction hash of the deferred event (lowercase).</summary>
    [MaxLength(128)]
    public string TransactionHash { get; set; } = string.Empty;

    /// <summary>Log index within the transaction.</summary>
    public int LogIndex { get; set; }

    /// <summary>Block number of the deferred event.</summary>
    public long BlockNumber { get; set; }

    /// <summary>Human-readable reason the event was deferred (e.g. "Job not found", "Invalid state Open for JobCompleted").</summary>
    [MaxLength(512)]
    public string DeferralReason { get; set; } = string.Empty;

    public DateTime DeferredAtUtc { get; set; } = DateTime.UtcNow;
}
