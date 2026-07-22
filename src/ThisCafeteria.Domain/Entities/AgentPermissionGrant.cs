using System.ComponentModel.DataAnnotations;

namespace ThisCafeteria.Domain.Entities;

/// <summary>
/// One exact-calldata delegation within an <see cref="AgentPermissionEpoch"/> — e.g. the
/// "approve" and "fund" delegations that together make up one agentic payment permission. The
/// per-operation amount doubles as the cumulative quota because every delegation is scoped with
/// <c>LimitedCallsEnforcer(1)</c> in the audited framework (a one-shot grant), so there is no
/// separate mutable running-total to track — the on-chain exactness of the calldata is the quota.
/// </summary>
public class AgentPermissionGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EpochId { get; set; }

    [MaxLength(128)]
    public string TargetAddress { get; set; } = string.Empty;

    /// <summary>4-byte function selector, e.g. "0xb61d27f6".</summary>
    [MaxLength(10)]
    public string Selector { get; set; } = string.Empty;

    /// <summary>ERC-20 token this delegation moves, if any (null for calls with no token argument).</summary>
    [MaxLength(128)]
    public string? TokenAddress { get; set; }

    /// <summary>Decimal-string uint256 exact amount authorized by this delegation's calldata.</summary>
    [MaxLength(80)]
    public string AmountWei { get; set; } = "0";

    /// <summary>EIP-712 delegation hash — the on-chain identity used by DelegationManager events.</summary>
    [MaxLength(66)]
    public string DelegationHash { get; set; } = string.Empty;

    /// <summary>Human label, e.g. "approve" or "fund".</summary>
    [MaxLength(64)]
    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
