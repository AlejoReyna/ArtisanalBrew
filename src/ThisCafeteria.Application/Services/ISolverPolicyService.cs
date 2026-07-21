using System.Numerics;

namespace ThisCafeteria.Application.Services;

/// <summary>
/// An ERC-7683 intent recovered from a source-chain submission, decoded from the submitting
/// transaction's own calldata rather than from the IntentSubmitted event — the event only carries
/// (orderId, user, destinationChainId, amountIn), not the full order the destination-chain
/// fillIntent call requires.
/// </summary>
public sealed record SolverIntent
{
    /// <summary>bytes32 orderId, lowercase hex.</summary>
    public string OrderId { get; init; } = string.Empty;

    public string User { get; init; } = string.Empty;
    public string SourceToken { get; init; } = string.Empty;
    public BigInteger AmountIn { get; init; }
    public BigInteger DestinationChainId { get; init; }
    public string DestinationToken { get; init; } = string.Empty;
    public string DestinationReceiver { get; init; } = string.Empty;
    public BigInteger MinAmountOut { get; init; }
    public BigInteger Deadline { get; init; }
    public BigInteger Nonce { get; init; }

    /// <summary>Zero address means any solver may fill; otherwise only this address may.</summary>
    public string AllowedSolver { get; init; } = string.Empty;

    public string SubmitTransactionHash { get; init; } = string.Empty;
}

public enum SolverDenialReason
{
    None = 0,
    NotConfigured,
    DisallowedTokenPair,
    AmountTooLarge,
    Expired,
    NotAllowedSolver,

    /// <summary>The most this solver would pay out (MaxOutputBps of amountIn) is below the order's minAmountOut.</summary>
    OutputBelowMinimum,

    InvalidRequest
}

public sealed record SolverPolicyDecision
{
    public bool Approved { get; init; }
    public SolverDenialReason Reason { get; init; }
    public string Detail { get; init; } = string.Empty;

    /// <summary>The amount this solver will pay out, when approved. Never exceeds the intent's amountIn.</summary>
    public BigInteger AmountOut { get; init; }

    public static SolverPolicyDecision Approve(BigInteger amountOut) =>
        new() { Approved = true, Reason = SolverDenialReason.None, AmountOut = amountOut };

    public static SolverPolicyDecision Deny(SolverDenialReason reason, string detail) =>
        new() { Approved = false, Reason = reason, Detail = detail };
}

/// <summary>
/// Decides whether the solver should fill an intent, and for how much. Fail-closed: an intent is
/// refused unless it positively matches an allowed token pair, is within the configured size limit,
/// has not expired, and (if the intent restricts solvers) names this solver specifically.
/// </summary>
public interface ISolverPolicyService
{
    SolverPolicyDecision Evaluate(SolverIntent intent, string solverAddress, DateTimeOffset now);
}
