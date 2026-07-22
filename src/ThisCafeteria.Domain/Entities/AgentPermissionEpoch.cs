using System.ComponentModel.DataAnnotations;

namespace ThisCafeteria.Domain.Entities;

public enum AgentPermissionEpochStatus
{
    /// <summary>Recorded but the owner's activating UserOperation has not yet been observed on-chain.</summary>
    PendingActivation = 0,
    Active = 1,
    Expired = 2,
    Revoked = 3
}

/// <summary>
/// Durable record of one MetaMask Delegation Framework permission epoch: the owner signs
/// caveat-scoped delegations for an agent and activates them by incrementing the account's
/// NonceEnforcer counter to <see cref="Epoch"/>; a later increment revokes the whole epoch.
///
/// This row is populated only after independently reading <c>NonceEnforcer.currentNonce</c> for
/// the modular account and confirming it equals <see cref="Epoch"/> — it mirrors on-chain state,
/// it does not create authorization. The chain's NonceEnforcer is the actual gate; this table
/// exists so the application can discover and display permission state without re-deriving it
/// from raw contract reads on every request.
/// </summary>
public class AgentPermissionEpoch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public string ChainKey { get; set; } = string.Empty;

    /// <summary>The modular account (delegator) this epoch belongs to.</summary>
    public Guid SmartAccountRecordId { get; set; }

    /// <summary>Denormalized modular account address, lowercased, for querying without a join.</summary>
    [MaxLength(128)]
    public string DelegatorAddress { get; set; } = string.Empty;

    /// <summary>Denormalized controlling EOA, lowercased.</summary>
    [MaxLength(128)]
    public string OwnerAddress { get; set; } = string.Empty;

    /// <summary>The agent's own deterministic HybridDeleGator address, lowercased.</summary>
    [MaxLength(128)]
    public string AgentAddress { get; set; } = string.Empty;

    /// <summary>Decimal-string uint256 NonceEnforcer epoch value this permission set is bound to.</summary>
    [MaxLength(80)]
    public string Epoch { get; set; } = string.Empty;

    /// <summary>TimestampEnforcer afterThreshold, as UTC.</summary>
    public DateTime ValidAfterUtc { get; set; }

    /// <summary>TimestampEnforcer beforeThreshold, as UTC — the permission's expiry.</summary>
    public DateTime ValidBeforeUtc { get; set; }

    public AgentPermissionEpochStatus Status { get; set; } = AgentPermissionEpochStatus.PendingActivation;

    public DateTime? InstalledAtUtc { get; set; }

    [MaxLength(128)]
    public string? InstalledTxHash { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    [MaxLength(128)]
    public string? RevokedTxHash { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Guards concurrent install/revoke races.</summary>
    [ConcurrencyCheck]
    public int ConcurrencyToken { get; set; }
}
