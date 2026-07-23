namespace ThisCafeteria.Application.Services;

public enum UserOperationSubmissionStatus
{
    /// <summary>The sponsorship policy refused to sign; nothing was ever sent to a bundler.</summary>
    Denied,

    /// <summary>The bundler refused the fully-signed operation outright (bad signature, unknown EntryPoint, etc.).</summary>
    Rejected,

    /// <summary>Accepted by the bundler but never mined within the poll window.</summary>
    TimedOut,

    /// <summary>Mined, but the EntryPoint's own UserOperationEvent recorded the inner call as failed.</summary>
    Reverted,

    /// <summary>Mined and independently verified against the EntryPoint's own event log — sponsorship usage was recorded.</summary>
    Confirmed
}

public sealed record UserOperationSubmissionResult
{
    public required UserOperationSubmissionStatus Status { get; init; }
    public string Detail { get; init; } = string.Empty;
    public string? UserOperationHash { get; init; }
    public string? TransactionHash { get; init; }
    public decimal CostUsd { get; init; }

    public bool Confirmed => Status == UserOperationSubmissionStatus.Confirmed;

    public static UserOperationSubmissionResult Denied(string detail) =>
        new() { Status = UserOperationSubmissionStatus.Denied, Detail = detail };
}

/// <summary>
/// Closes the gap between "a paymaster signature was produced" (<see cref="IUserOperationSponsor"/>)
/// and an actually-mined, independently-verified sponsored transaction: submits the fully-signed
/// operation through the configured bundler (<see cref="IBundlerClient"/>), polls for a receipt, and
/// verifies the result server-side against the canonical EntryPoint's own on-chain event — the same
/// "decode the event, match it exactly, don't trust a success flag alone" pattern this repo already
/// uses for liquid-staking and escrow transactions — before ever debiting the sponsorship grant.
///
/// This never calls EntryPoint.handleOps directly; only <see cref="IBundlerClient"/> talks to a
/// bundler, and only a bundler talks to the EntryPoint.
/// </summary>
public interface IUserOperationSubmitter
{
    /// <summary>
    /// <paramref name="sponsorship"/> must come from a prior, already-approved
    /// <see cref="IUserOperationSponsor.SponsorAsync"/> call for this exact operation —
    /// deliberately not re-derived here. v0.7's UserOperation hash covers a hash of
    /// <c>paymasterAndData</c>, and <c>paymasterAndData</c> embeds a <c>validUntil</c> timestamp
    /// fixed at the moment it was signed; calling <c>SponsorAsync</c> again here would mint a new
    /// timestamp and therefore a different <c>paymasterAndData</c> than the one <paramref
    /// name="signature"/> was actually computed over, invalidating it. Re-sponsorship cannot be used
    /// to bypass policy either way: <paramref name="sponsorship"/>'s <c>paymasterAndData</c> is only
    /// valid on-chain if it carries this server's own paymaster signing key's signature, which a
    /// caller cannot forge — the canonical paymaster contract is the actual arbiter.
    ///
    /// <paramref name="signature"/> is the account owner's ECDSA signature over
    /// <c>EntryPoint.getUserOpHash(...)</c> for this exact operation plus
    /// <paramref name="sponsorship"/>'s <c>paymasterAndData</c>, produced by the caller (the smart
    /// account's owner) after sponsorship exists.
    /// </summary>
    Task<UserOperationSubmissionResult> SubmitAsync(SponsoredUserOperation operation, SponsorshipSignature sponsorship, string signature, CancellationToken cancellationToken = default);
}
