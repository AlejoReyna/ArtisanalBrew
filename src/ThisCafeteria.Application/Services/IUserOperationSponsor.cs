namespace ThisCafeteria.Application.Services;

/// <summary>
/// A UserOperation awaiting a sponsorship decision.
///
/// <see cref="TargetAddress"/> and <see cref="Selector"/> are required, not optional. That is
/// deliberate: <see cref="ISmartAccountService.HasSufficientSponsorshipQuotaAsync"/> carries
/// neither, so it can only check budget and validity. Making them mandatory here means it is not
/// possible to obtain a paymaster signature without the target and selector having been checked —
/// the wrong-target/wrong-selector hole is closed by the shape of the type rather than by a
/// comment asking callers to be careful.
///
/// There is deliberately no cost or gas-estimate field here. Cost is derived by
/// <c>UserOperationSponsor</c> from <see cref="IUserOperationSimulator"/> — the canonical
/// EntryPoint's own gas simulation — rather than accepted from the caller. A budget enforced
/// against a self-reported number is advisory, not a control.
/// </summary>
public sealed record SponsoredUserOperation
{
    public string ChainKey { get; init; } = string.Empty;
    public string OwnerAddress { get; init; } = string.Empty;

    /// <summary>Smart account the operation runs as.</summary>
    public string Sender { get; init; } = string.Empty;

    public System.Numerics.BigInteger Nonce { get; init; }
    public string InitCode { get; init; } = "0x";
    public string CallData { get; init; } = "0x";

    /// <summary>bytes32: verificationGasLimit (16 bytes) | callGasLimit (16 bytes).</summary>
    public string AccountGasLimits { get; init; } = string.Empty;

    public System.Numerics.BigInteger PreVerificationGas { get; init; }

    /// <summary>bytes32: maxPriorityFeePerGas (16 bytes) | maxFeePerGas (16 bytes).</summary>
    public string GasFees { get; init; } = string.Empty;

    /// <summary>Contract the inner call targets. Required — see the type remarks.</summary>
    public required string TargetAddress { get; init; }

    /// <summary>4-byte selector of the inner call. Required — see the type remarks.</summary>
    public required string Selector { get; init; }
}

public sealed record SponsorshipSignature
{
    public bool Approved { get; init; }

    /// <summary>Denial reason when <see cref="Approved"/> is false.</summary>
    public SponsorshipDenialReason Reason { get; init; }

    public string Detail { get; init; } = string.Empty;

    /// <summary>
    /// Fully-formed v0.7 paymasterAndData, empty when denied:
    /// paymaster(20) | verificationGasLimit(16) | postOpGasLimit(16) | validUntil+validAfter(64) | signature.
    /// </summary>
    public string PaymasterAndData { get; init; } = string.Empty;

    /// <summary>Cost charged against the owner's grant, derived from gas rather than caller-supplied.</summary>
    public decimal CostUsd { get; init; }

    public static SponsorshipSignature Deny(SponsorshipDenialReason reason, string detail) =>
        new() { Approved = false, Reason = reason, Detail = detail };
}

/// <summary>
/// Produces paymaster sponsorship signatures, but only for operations the sponsorship policy
/// authorises. This is the component that turns the policy's verdict into something the
/// canonical VerifyingPaymaster will accept on-chain.
///
/// Fail-closed: without a configured verifying signer key it signs nothing, and a policy denial
/// never yields a signature.
/// </summary>
public interface IUserOperationSponsor
{
    /// <summary>
    /// Prices the operation from its gas, asks the policy, and signs only on approval.
    /// Does not debit the grant — call <see cref="ISponsorshipPolicyService.RecordUsageAsync"/>
    /// once the operation is actually submitted.
    /// </summary>
    Task<SponsorshipSignature> SponsorAsync(SponsoredUserOperation operation, CancellationToken cancellationToken = default);
}
