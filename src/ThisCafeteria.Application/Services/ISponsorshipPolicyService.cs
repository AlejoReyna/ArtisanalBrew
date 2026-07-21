namespace ThisCafeteria.Application.Services;

/// <summary>
/// Why a sponsorship request was refused. These map directly onto the Phase 4 gate requirement
/// that "over-budget, wrong-target, wrong-selector, expired, and revoked operations fail".
/// </summary>
public enum SponsorshipDenialReason
{
    None = 0,

    /// <summary>No paymaster/EntryPoint configured for the chain, or sponsorship is disabled.</summary>
    NotConfigured,

    /// <summary>The owner has no sponsorship grant on this chain.</summary>
    NoGrant,

    /// <summary>The grant was explicitly revoked.</summary>
    Revoked,

    /// <summary>The grant's validity window has passed.</summary>
    Expired,

    /// <summary>The grant's validity window has not started yet.</summary>
    NotYetValid,

    /// <summary>The operation would push cumulative spend past the grant budget.</summary>
    OverBudget,

    /// <summary>The single operation exceeds the grant's per-operation cap.</summary>
    OperationTooExpensive,

    /// <summary>The operation targets a contract that is not on the allowlist.</summary>
    DisallowedTarget,

    /// <summary>The operation invokes a function selector that is not on the allowlist.</summary>
    DisallowedSelector,

    /// <summary>The request was malformed (e.g. negative cost, missing owner).</summary>
    InvalidRequest,

    /// <summary>
    /// Gas simulation against the canonical EntryPoint failed or reverted — a real validation
    /// failure (e.g. bad nonce, expired window), not a signature mismatch. An unsimulated
    /// operation has no trustworthy cost, so it cannot be sponsored.
    /// </summary>
    SimulationFailed
}

/// <summary>A sponsorship request. Target/selector are optional for coarse budget-only checks.</summary>
public sealed record SponsorshipRequest
{
    public string ChainKey { get; init; } = string.Empty;
    public string OwnerAddress { get; init; } = string.Empty;
    public decimal EstimatedCostUsd { get; init; }

    /// <summary>Contract the UserOperation's inner call targets. Null skips target checking.</summary>
    public string? TargetAddress { get; init; }

    /// <summary>4-byte selector of the inner call (e.g. "0xb61d27f6"). Null skips selector checking.</summary>
    public string? Selector { get; init; }
}

public sealed record SponsorshipDecision
{
    public bool Approved { get; init; }
    public SponsorshipDenialReason Reason { get; init; }
    public string Detail { get; init; } = string.Empty;

    /// <summary>Budget left on the grant after this operation, when approved.</summary>
    public decimal RemainingUsd { get; init; }

    public static SponsorshipDecision Approve(decimal remainingUsd) =>
        new() { Approved = true, Reason = SponsorshipDenialReason.None, RemainingUsd = remainingUsd };

    public static SponsorshipDecision Deny(SponsorshipDenialReason reason, string detail) =>
        new() { Approved = false, Reason = reason, Detail = detail };
}

/// <summary>
/// Decides whether an ERC-4337 UserOperation may be sponsored, and records what was spent.
///
/// The canonical VerifyingPaymaster enforces only "the verifying signer approved this operation".
/// This service is the policy that decides whether to produce that signature, so it is the actual
/// safety boundary for sponsored gas. It is fail-closed: anything it cannot positively authorise
/// is denied.
/// </summary>
public interface ISponsorshipPolicyService
{
    Task<SponsorshipDecision> EvaluateAsync(SponsorshipRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Debits actual cost against the owner's grant and writes an audit row. Throws if no valid
    /// grant exists, so usage can never be recorded against a revoked or absent grant.
    /// </summary>
    Task RecordUsageAsync(SponsorshipRequest request, CancellationToken cancellationToken = default);

    /// <summary>Revokes the owner's grant. Idempotent; revoking an absent grant is a no-op.</summary>
    Task RevokeAsync(string chainKey, string ownerAddress, CancellationToken cancellationToken = default);
}
