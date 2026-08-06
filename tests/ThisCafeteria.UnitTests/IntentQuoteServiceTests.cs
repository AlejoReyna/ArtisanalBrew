using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Services;
using Xunit;

namespace ThisCafeteria.UnitTests;

/// <summary>
/// IntentQuoteService deliberately contains no pricing logic of its own — it delegates entirely to
/// ISolverPolicyService, the exact same policy CrossChainSolverWorker uses to decide real fills.
/// These tests confirm that delegation is faithful (approved cases return the policy's own
/// AmountOut, denied cases return the policy's own reason), not that the pricing math itself is
/// correct — that's SolverPolicyServiceTests' job. The live cross-stack proof that a previewed
/// quote matches what the standing solver actually pays is in
/// contracts/evm/scripts/two-node-standing-solver-check.ts.
/// </summary>
public class IntentQuoteServiceTests
{
    private const string SourceToken = "0xdc64a140aa3e981100a9beca4e685f962f0cf6c9";
    private const string DestToken = "0x70997970c51812dc3a010c7d01b50e0d17dc79c8";

    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    private static CrossChainSolverOptions EnabledOptions => new()
    {
        Enabled = true,
        SourceChainKey = "arbitrumLocal",
        SourceResolverAddress = "0x1111111111111111111111111111111111111111",
        DestinationChainKey = "baseLocal",
        DestinationResolverAddress = "0x2222222222222222222222222222222222222222",
        SolverPrivateKey = "0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80",
        AllowedTokenPairs = [new CrossChainSolverOptions.TokenPair { SourceToken = SourceToken, DestinationToken = DestToken }],
        MaxAmountIn = 100m,
        MaxOutputBps = 9_500
    };

    private static IntentQuoteService CreateService(CrossChainSolverOptions? options = null)
    {
        var opts = options ?? EnabledOptions;
        var time = new FakeTimeProvider(Now);
        return new IntentQuoteService(new SolverPolicyService(opts), opts, time);
    }

    private static readonly System.Numerics.BigInteger TenEth = (System.Numerics.BigInteger)10_000_000_000_000_000_000m;

    [Fact]
    public void GetQuote_AllowedPair_ReturnsFillableWithPolicyComputedAmountOut()
    {
        var quote = CreateService().GetQuote(new IntentQuoteRequest
        {
            SourceToken = SourceToken,
            DestinationToken = DestToken,
            AmountIn = TenEth
        });

        quote.Fillable.Should().BeTrue();
        // 9500 bps of 10 ETH — must match SolverPolicyService's own math exactly, not an approximation.
        quote.AmountOut.Should().Be((System.Numerics.BigInteger)9_500_000_000_000_000_000m);
        quote.SourceChainKey.Should().Be("arbitrumLocal");
        quote.DestinationChainKey.Should().Be("baseLocal");
    }

    [Fact]
    public void GetQuote_DisallowedPair_ReturnsNotFillableWithPolicyReason()
    {
        var quote = CreateService().GetQuote(new IntentQuoteRequest
        {
            SourceToken = "0x0000000000000000000000000000000000dead",
            DestinationToken = DestToken,
            AmountIn = TenEth
        });

        quote.Fillable.Should().BeFalse();
        quote.DenialReason.Should().Be(SolverDenialReason.DisallowedTokenPair);
    }

    [Fact]
    public void GetQuote_AmountExceedsMax_ReturnsNotFillable()
    {
        var quote = CreateService().GetQuote(new IntentQuoteRequest
        {
            SourceToken = SourceToken,
            DestinationToken = DestToken,
            AmountIn = (System.Numerics.BigInteger)(1000m * 1_000_000_000_000_000_000m)
        });

        quote.Fillable.Should().BeFalse();
        quote.DenialReason.Should().Be(SolverDenialReason.AmountTooLarge);
    }

    [Fact]
    public void GetQuote_SolverDisabled_ReturnsNotConfigured()
    {
        var quote = CreateService(EnabledOptions with { Enabled = false }).GetQuote(new IntentQuoteRequest
        {
            SourceToken = SourceToken,
            DestinationToken = DestToken,
            AmountIn = TenEth
        });

        quote.Fillable.Should().BeFalse();
        quote.DenialReason.Should().Be(SolverDenialReason.NotConfigured);
    }

    [Fact]
    public void GetQuote_WorksWithoutResolverAddressesOrPrivateKey()
    {
        // A read-only quote-preview process (e.g. the Web API, separate from the Worker that
        // actually executes fills) has no legitimate reason to hold the solver's private key.
        // Pricing must not require it — only CanPrice (Enabled + chain keys), not CanOperate
        // (also resolver addresses + a signing key).
        var options = EnabledOptions with
        {
            SourceResolverAddress = string.Empty,
            DestinationResolverAddress = string.Empty,
            SolverPrivateKey = string.Empty
        };

        var quote = CreateService(options).GetQuote(new IntentQuoteRequest
        {
            SourceToken = SourceToken,
            DestinationToken = DestToken,
            AmountIn = TenEth
        });

        quote.Fillable.Should().BeTrue("pricing needs no resolver address or signing key, only chain keys and the token/amount policy");
    }

    [Fact]
    public void GetQuote_DoesNotPreConstrainMinAmountOut_SoTheQuoteItselfIsTheDiscovery()
    {
        // At 10000 bps (no spread) the full amountIn should come back, proving the synthetic
        // intent's MinAmountOut=0 never clips what the policy would actually pay.
        var options = EnabledOptions with { MaxOutputBps = 10_000 };
        var quote = CreateService(options).GetQuote(new IntentQuoteRequest
        {
            SourceToken = SourceToken,
            DestinationToken = DestToken,
            AmountIn = TenEth
        });

        quote.Fillable.Should().BeTrue();
        quote.AmountOut.Should().Be(TenEth);
    }
}
