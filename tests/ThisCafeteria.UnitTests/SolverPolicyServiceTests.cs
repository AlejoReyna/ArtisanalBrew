using FluentAssertions;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Services;
using Xunit;

namespace ThisCafeteria.UnitTests;

/// <summary>
/// Fail-closed behaviour of the cross-chain solver's policy. The live cross-chain proof — a real
/// intent submitted on a genuinely separate source node, decoded from real transaction calldata,
/// and filled by the standing CrossChainSolverWorker without any script performing the fill — is
/// in contracts/evm/scripts/two-node-standing-solver-check.ts; a chain-free unit test cannot tell a
/// correct fill from a coincidentally-matching one.
/// </summary>
public class SolverPolicyServiceTests
{
    private const string SourceToken = "0xdc64a140aa3e981100a9beca4e685f962f0cf6c9";
    private const string DestToken = "0x70997970c51812dc3a010c7d01b50e0d17dc79c8";
    private const string SolverAddress = "0xf39fd6e51aad88f6f4ce6ab8827279cfffb92266";
    private const string ZeroAddress = "0x0000000000000000000000000000000000000000";

    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    private static CrossChainSolverOptions EnabledOptions => new()
    {
        Enabled = true,
        SourceChainKey = "arbitrumLocal",
        SourceResolverAddress = "0x1111111111111111111111111111111111111111",
        DestinationChainKey = "baseLocal",
        DestinationResolverAddress = "0x2222222222222222222222222222222222222222",
        SolverPrivateKey = "0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80",
        AllowedTokenPairs = [new CrossChainSolverOptions.TokenPair(SourceToken, DestToken)],
        MaxAmountIn = 100m,
        MaxOutputBps = 10_000
    };

    private static SolverIntent Intent(
        decimal amountInEth = 10m,
        decimal minAmountOutEth = 10m,
        long deadlineOffsetSeconds = 3600,
        string? allowedSolver = null,
        string? sourceToken = null,
        string? destToken = null) => new()
    {
        OrderId = "0xabc123",
        User = "0x3c44cdddb6a900fa2b585dd299e03d12fa4293bc",
        SourceToken = sourceToken ?? SourceToken,
        AmountIn = (System.Numerics.BigInteger)(amountInEth * 1_000_000_000_000_000_000m),
        DestinationChainId = 84532,
        DestinationToken = destToken ?? DestToken,
        DestinationReceiver = "0x90f79bf6eb2c4f870365e785982e1f101e93b906",
        MinAmountOut = (System.Numerics.BigInteger)(minAmountOutEth * 1_000_000_000_000_000_000m),
        Deadline = Now.ToUnixTimeSeconds() + deadlineOffsetSeconds,
        Nonce = 1,
        AllowedSolver = allowedSolver ?? ZeroAddress,
        SubmitTransactionHash = "0xdeadbeef"
    };

    [Fact]
    public void Evaluate_ValidIntent_IsApproved()
    {
        var decision = new SolverPolicyService(EnabledOptions).Evaluate(Intent(), SolverAddress, Now);

        decision.Approved.Should().BeTrue();
        decision.AmountOut.Should().Be((System.Numerics.BigInteger)10_000_000_000_000_000_000m, "10000 bps of a 10 ETH amountIn is the full amountIn");
    }

    [Fact]
    public void Evaluate_SolverDisabled_IsDenied()
    {
        var options = EnabledOptions with { Enabled = false };
        var decision = new SolverPolicyService(options).Evaluate(Intent(), SolverAddress, Now);

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SolverDenialReason.NotConfigured);
    }

    [Fact]
    public void Evaluate_EmptyAllowlist_DeniesEverything()
    {
        var options = EnabledOptions with { AllowedTokenPairs = [] };
        var decision = new SolverPolicyService(options).Evaluate(Intent(), SolverAddress, Now);

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SolverDenialReason.DisallowedTokenPair);
    }

    [Fact]
    public void Evaluate_UnlistedTokenPair_IsDenied()
    {
        var decision = new SolverPolicyService(EnabledOptions).Evaluate(
            Intent(sourceToken: "0x0000000000000000000000000000000000dead"), SolverAddress, Now);

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SolverDenialReason.DisallowedTokenPair);
    }

    [Fact]
    public void Evaluate_AmountExceedsMax_IsDenied()
    {
        var decision = new SolverPolicyService(EnabledOptions).Evaluate(Intent(amountInEth: 1000m), SolverAddress, Now);

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SolverDenialReason.AmountTooLarge);
    }

    [Fact]
    public void Evaluate_ExpiredDeadline_IsDenied()
    {
        var decision = new SolverPolicyService(EnabledOptions).Evaluate(Intent(deadlineOffsetSeconds: -10), SolverAddress, Now);

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SolverDenialReason.Expired);
    }

    [Fact]
    public void Evaluate_RestrictedToAnotherSolver_IsDenied()
    {
        var decision = new SolverPolicyService(EnabledOptions).Evaluate(
            Intent(allowedSolver: "0x0000000000000000000000000000000000beef"), SolverAddress, Now);

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SolverDenialReason.NotAllowedSolver);
    }

    [Fact]
    public void Evaluate_RestrictedToThisSolver_IsApproved()
    {
        var decision = new SolverPolicyService(EnabledOptions).Evaluate(
            Intent(allowedSolver: SolverAddress), SolverAddress, Now);

        decision.Approved.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ComputedOutputBelowMinAmountOut_IsDenied()
    {
        // At 9000 bps the solver would pay 9 ETH on a 10 ETH amountIn, below the 10 ETH minAmountOut.
        var options = EnabledOptions with { MaxOutputBps = 9_000 };
        var decision = new SolverPolicyService(options).Evaluate(Intent(), SolverAddress, Now);

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SolverDenialReason.OutputBelowMinimum);
    }

    [Fact]
    public void Evaluate_SpreadWithinMinAmountOut_IsApprovedAtReducedAmount()
    {
        // 9500 bps of 10 ETH = 9.5 ETH, still >= a 9 ETH minAmountOut.
        var options = EnabledOptions with { MaxOutputBps = 9_500 };
        var decision = new SolverPolicyService(options).Evaluate(Intent(minAmountOutEth: 9m), SolverAddress, Now);

        decision.Approved.Should().BeTrue();
        decision.AmountOut.Should().Be((System.Numerics.BigInteger)9_500_000_000_000_000_000m);
    }

    [Fact]
    public void Evaluate_ZeroAmountIn_IsRejectedAsInvalid()
    {
        var decision = new SolverPolicyService(EnabledOptions).Evaluate(Intent(amountInEth: 0m), SolverAddress, Now);

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SolverDenialReason.InvalidRequest);
    }

    [Fact]
    public void Evaluate_TokenPairMatchIsCaseInsensitive()
    {
        var decision = new SolverPolicyService(EnabledOptions).Evaluate(
            Intent(sourceToken: SourceToken.ToUpperInvariant(), destToken: DestToken.ToUpperInvariant()),
            SolverAddress, Now);

        decision.Approved.Should().BeTrue();
    }
}
