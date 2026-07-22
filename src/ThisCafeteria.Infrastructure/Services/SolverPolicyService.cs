using System.Numerics;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Infrastructure.Services;

/// <summary>Fail-closed solver policy. See <see cref="ISolverPolicyService"/> for the rationale.</summary>
public sealed class SolverPolicyService(CrossChainSolverOptions options) : ISolverPolicyService
{
    public SolverPolicyDecision Evaluate(SolverIntent intent, string solverAddress, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(intent.OrderId) || string.IsNullOrWhiteSpace(intent.SourceToken)
            || string.IsNullOrWhiteSpace(intent.DestinationToken) || intent.AmountIn <= BigInteger.Zero)
        {
            return SolverPolicyDecision.Deny(SolverDenialReason.InvalidRequest, "Intent is missing required fields or has a non-positive amountIn.");
        }

        if (!options.CanPrice)
        {
            return SolverPolicyDecision.Deny(SolverDenialReason.NotConfigured, "Cross-chain solver is not configured.");
        }

        if (!options.IsTokenPairAllowed(intent.SourceToken, intent.DestinationToken))
        {
            return SolverPolicyDecision.Deny(SolverDenialReason.DisallowedTokenPair,
                $"Token pair ({intent.SourceToken} -> {intent.DestinationToken}) is not on the solver's allowlist.");
        }

        var maxAmountInBaseUnits = (BigInteger)(options.MaxAmountIn * 1_000_000_000_000_000_000m);
        if (intent.AmountIn > maxAmountInBaseUnits)
        {
            return SolverPolicyDecision.Deny(SolverDenialReason.AmountTooLarge,
                $"amountIn {intent.AmountIn} exceeds the solver's configured maximum {maxAmountInBaseUnits}.");
        }

        if (now.ToUnixTimeSeconds() >= (long)intent.Deadline)
        {
            return SolverPolicyDecision.Deny(SolverDenialReason.Expired, $"Intent deadline {intent.Deadline} has passed.");
        }

        var zeroAddress = "0x0000000000000000000000000000000000000000";
        if (!string.Equals(intent.AllowedSolver, zeroAddress, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(intent.AllowedSolver, solverAddress, StringComparison.OrdinalIgnoreCase))
        {
            return SolverPolicyDecision.Deny(SolverDenialReason.NotAllowedSolver,
                $"Intent restricts filling to {intent.AllowedSolver}, this solver is {solverAddress}.");
        }

        var amountOut = intent.AmountIn * options.MaxOutputBps / 10_000;
        if (amountOut < intent.MinAmountOut)
        {
            return SolverPolicyDecision.Deny(SolverDenialReason.OutputBelowMinimum,
                $"At {options.MaxOutputBps} bps this solver would pay {amountOut}, below the required minAmountOut {intent.MinAmountOut}.");
        }

        return SolverPolicyDecision.Approve(amountOut);
    }
}
