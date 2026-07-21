using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Infrastructure.Services;

/// <summary>See <see cref="IIntentQuoteService"/> — this wraps <see cref="ISolverPolicyService"/>, it does not repeat its logic.</summary>
public sealed class IntentQuoteService(
    ISolverPolicyService policy,
    CrossChainSolverOptions options,
    TimeProvider timeProvider) : IIntentQuoteService
{
    private static readonly string ZeroAddress = "0x0000000000000000000000000000000000000000";

    // A well-formed but obviously-synthetic bytes32 sentinel. ISolverPolicyService.Evaluate
    // rejects an empty OrderId as InvalidRequest — correctly so for the real fill path, where an
    // empty OrderId would mean CrossChainIntentProvider's calldata decoding failed upstream. A
    // preview has no real order yet, so it supplies this sentinel rather than weakening that check.
    private static readonly string PreviewOrderIdSentinel = "0x" + new string('0', 63) + "1";

    public IntentQuoteResult GetQuote(IntentQuoteRequest request)
    {
        // No separate check here: ISolverPolicyService.Evaluate itself gates on CanPrice (not
        // CanOperate) precisely so this process never needs the solver's resolver addresses or
        // private key just to price a route.
        var now = timeProvider.GetUtcNow();

        // A synthetic intent for pricing purposes only — it is never submitted on-chain. Deadline
        // is set far enough out that a real intent submitted moments after this quote would not
        // itself be treated as expired; allowedSolver is the zero address (any solver may fill) so
        // the quote reflects the general case rather than one hypothetical solver identity.
        var syntheticIntent = new SolverIntent
        {
            OrderId = PreviewOrderIdSentinel,
            User = ZeroAddress,
            SourceToken = request.SourceToken,
            AmountIn = request.AmountIn,
            DestinationChainId = 0,
            DestinationToken = request.DestinationToken,
            DestinationReceiver = ZeroAddress,
            MinAmountOut = 0, // the quote itself IS the discovery of the achievable output — do not pre-constrain it
            Deadline = now.AddHours(1).ToUnixTimeSeconds(),
            Nonce = 0,
            AllowedSolver = ZeroAddress
        };

        var solverAddress = ZeroAddress; // policy's AllowedSolver check passes for the zero address regardless of who asks
        var decision = policy.Evaluate(syntheticIntent, solverAddress, now);

        return decision.Approved
            ? IntentQuoteResult.Quotable(decision.AmountOut, options.SourceChainKey, options.DestinationChainKey)
            : IntentQuoteResult.NotQuotable(decision.Reason, decision.Detail);
    }
}
