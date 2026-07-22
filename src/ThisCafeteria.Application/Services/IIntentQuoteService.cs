using System.Numerics;

namespace ThisCafeteria.Application.Services;

public sealed record IntentQuoteRequest
{
    public string SourceToken { get; init; } = string.Empty;
    public string DestinationToken { get; init; } = string.Empty;
    public BigInteger AmountIn { get; init; }
}

public sealed record IntentQuoteResult
{
    public bool Fillable { get; init; }
    public BigInteger AmountOut { get; init; }
    public SolverDenialReason? DenialReason { get; init; }
    public string Detail { get; init; } = string.Empty;

    /// <summary>Chain keys the quote applies to — the caller must submit the real intent on these.</summary>
    public string SourceChainKey { get; init; } = string.Empty;
    public string DestinationChainKey { get; init; } = string.Empty;

    public static IntentQuoteResult Quotable(BigInteger amountOut, string sourceChainKey, string destinationChainKey) =>
        new() { Fillable = true, AmountOut = amountOut, SourceChainKey = sourceChainKey, DestinationChainKey = destinationChainKey };

    public static IntentQuoteResult NotQuotable(SolverDenialReason reason, string detail) =>
        new() { Fillable = false, DenialReason = reason, Detail = detail };
}

/// <summary>
/// Previews what the standing cross-chain solver would pay out for a hypothetical intent, before
/// the caller ever submits one on-chain.
///
/// This deliberately does not reimplement any pricing logic — it evaluates the request through the
/// exact same <see cref="ISolverPolicyService"/> the real <c>CrossChainSolverWorker</c> uses. A
/// quote computed by separate logic could drift from what the solver actually does when the real
/// intent shows up; delegating to the same policy means the preview and the outcome can never
/// disagree with each other, only with reality if the solver's own configuration changes between
/// the quote and the submission (a real, disclosed possibility of any quote, not a bug here).
/// </summary>
public interface IIntentQuoteService
{
    IntentQuoteResult GetQuote(IntentQuoteRequest request);
}
