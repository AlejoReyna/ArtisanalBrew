using System.ComponentModel.DataAnnotations;

namespace ThisCafeteria.Domain.Entities;

/// <summary>
/// Durable, idempotent record of an ERC-7683 intent this solver has evaluated. Recorded whether
/// the outcome was a fill or a policy denial, so the same orderId is never re-evaluated after a
/// worker restart and every decision is auditable after the fact.
///
/// Identity is (SourceChainKey, SourceResolverAddress, OrderId) — mirrors the applied/deferred
/// event identity pattern used by the escrow reconciliation worker.
/// </summary>
public class CrossChainSolverFill
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public string SourceChainKey { get; set; } = string.Empty;

    [MaxLength(128)]
    public string SourceResolverAddress { get; set; } = string.Empty;

    /// <summary>bytes32 orderId, lowercase hex.</summary>
    [MaxLength(66)]
    public string OrderId { get; set; } = string.Empty;

    [MaxLength(128)]
    public string SubmitTransactionHash { get; set; } = string.Empty;

    public bool Filled { get; set; }

    /// <summary>Populated when Filled is true.</summary>
    [MaxLength(128)]
    public string? FillTransactionHash { get; set; }

    /// <summary>Populated when Filled is false — why the policy refused to fill this intent.</summary>
    [MaxLength(256)]
    public string? DenialReason { get; set; }

    public DateTime EvaluatedAtUtc { get; set; } = DateTime.UtcNow;
}
