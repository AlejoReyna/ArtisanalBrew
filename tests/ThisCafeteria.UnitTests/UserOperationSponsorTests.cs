using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Services;
using Xunit;

namespace ThisCafeteria.UnitTests;

/// <summary>
/// Fail-closed behaviour of the sponsorship signer. Every case here returns before any RPC call,
/// so these run without a chain; the happy path is proven cross-stack against the real on-chain
/// paymaster by contracts/evm/scripts/crossstack-sponsor-check.ts, since a signature that only
/// this codebase agrees with would prove nothing.
/// </summary>
public class UserOperationSponsorTests
{
    private const string ChainKey = "evm-local";
    private const string Owner = "0xf39fd6e51aad88f6f4ce6ab8827279cfffb92266";
    private const string Target = "0xa51c1fc2f0d1a1b8494ed1fe312d7c3a78ed91c0";
    private const string Selector = "0xb61d27f6";
    // Hardhat's first well-known development account. This key is published in Hardhat's own
    // documentation and controls nothing outside a local test node - it is not a secret.
    private const string DevKey = "0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero));

    private sealed class StubChainRegistry(params ChainDefinition[] chains) : IChainRegistry
    {
        public string DefaultChainKey => chains[0].Key;
        public IReadOnlyList<ChainDefinition> All { get; } = chains;
        public bool TryGet(string key, out ChainDefinition definition)
        {
            definition = All.FirstOrDefault(c => c.Key == key)!;
            return definition is not null;
        }
        public ChainDefinition GetRequired(string key) => TryGet(key, out var d) ? d : throw new KeyNotFoundException(key);
    }

    private sealed class StubPolicy(SponsorshipDecision decision) : ISponsorshipPolicyService
    {
        public decimal? SeenCostUsd { get; private set; }
        public string? SeenTarget { get; private set; }
        public string? SeenSelector { get; private set; }

        public Task<SponsorshipDecision> EvaluateAsync(SponsorshipRequest request, CancellationToken cancellationToken = default)
        {
            SeenCostUsd = request.EstimatedCostUsd;
            SeenTarget = request.TargetAddress;
            SeenSelector = request.Selector;
            return Task.FromResult(decision);
        }

        public Task RecordUsageAsync(SponsorshipRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevokeAsync(string chainKey, string ownerAddress, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static ChainDefinition PaymasterChain => new()
    {
        Key = ChainKey,
        Family = ChainFamily.Evm,
        EvmChainId = 31337,
        EvmChainIdHex = "0x7a69",
        PublicRpcUrl = "http://127.0.0.1:8545",
        Deployment = new ChainDeployment
        {
            EntryPoint = "0x8a791620dd6260079bf849dc5567adc3f2fdc318",
            AccountFactory = "0x610178da211fef7d417bc0e6fed39f05609ad788",
            VerifyingPaymaster = "0xb7f8bc63bbcad18155201308c8f3540b07f84f5e"
        }
    };

    private static SponsorshipPolicyOptions SigningOptions => new()
    {
        Enabled = true,
        AllowedTargets = new[] { Target },
        AllowedSelectors = new[] { Selector },
        VerifyingSignerPrivateKey = DevKey,
        NativeCurrencyUsdRate = 3000m
    };

    private UserOperationSponsor CreateSponsor(SponsorshipPolicyOptions options, ISponsorshipPolicyService policy) =>
        new(new StubChainRegistry(PaymasterChain), policy, options, _time, NullLogger<UserOperationSponsor>.Instance);

    private static SponsoredUserOperation Operation(System.Numerics.BigInteger? gas = null, System.Numerics.BigInteger? gasPrice = null) => new()
    {
        ChainKey = ChainKey,
        OwnerAddress = Owner,
        Sender = "0x93e957812b6ce6e7100b0b743f39376838be9920",
        Nonce = 0,
        InitCode = "0x",
        CallData = "0xb61d27f6",
        AccountGasLimits = "0x000000000000000000000000000f4240000000000000000000000000000f4240",
        PreVerificationGas = 100_000,
        GasFees = "0x0000000000000000000000003b9aca00000000000000000000000002540be400",
        TargetAddress = Target,
        Selector = Selector,
        EstimatedGas = gas ?? 2_000_000,
        GasPriceWei = gasPrice ?? 10_000_000_000
    };

    [Fact]
    public async Task Sponsor_WithoutSignerKey_RefusesToSign()
    {
        var options = SigningOptions with { VerifyingSignerPrivateKey = string.Empty };
        var policy = new StubPolicy(SponsorshipDecision.Approve(100m));

        var result = await CreateSponsor(options, policy).SponsorAsync(Operation());

        result.Approved.Should().BeFalse();
        result.Reason.Should().Be(SponsorshipDenialReason.NotConfigured);
        result.PaymasterAndData.Should().BeEmpty();
    }

    [Fact]
    public async Task Sponsor_WithoutNativeUsdRate_RefusesToSign()
    {
        // Gas that cannot be priced would make the budget meaningless, so signing must stop.
        var options = SigningOptions with { NativeCurrencyUsdRate = 0m };
        var policy = new StubPolicy(SponsorshipDecision.Approve(100m));

        var result = await CreateSponsor(options, policy).SponsorAsync(Operation());

        result.Approved.Should().BeFalse();
        result.Reason.Should().Be(SponsorshipDenialReason.NotConfigured);
        result.PaymasterAndData.Should().BeEmpty();
    }

    [Fact]
    public async Task Sponsor_WhenPolicyDisabled_RefusesToSign()
    {
        var options = SigningOptions with { Enabled = false };
        var policy = new StubPolicy(SponsorshipDecision.Approve(100m));

        var result = await CreateSponsor(options, policy).SponsorAsync(Operation());

        result.Approved.Should().BeFalse();
        result.Reason.Should().Be(SponsorshipDenialReason.NotConfigured);
    }

    [Fact]
    public async Task Sponsor_WhenPolicyDenies_ProducesNoSignature()
    {
        var policy = new StubPolicy(SponsorshipDecision.Deny(SponsorshipDenialReason.OverBudget, "no budget"));

        var result = await CreateSponsor(SigningOptions, policy).SponsorAsync(Operation());

        result.Approved.Should().BeFalse();
        result.Reason.Should().Be(SponsorshipDenialReason.OverBudget);
        result.PaymasterAndData.Should().BeEmpty("a denied operation must never yield a paymaster signature");
    }

    [Fact]
    public async Task Sponsor_PricesGasItself_RatherThanTrustingCaller()
    {
        var policy = new StubPolicy(SponsorshipDecision.Deny(SponsorshipDenialReason.OverBudget, "stop before RPC"));

        // 2,000,000 gas * 10 gwei = 0.02 ETH; at 3000 USD/ETH that is 60 USD.
        await CreateSponsor(SigningOptions, policy).SponsorAsync(Operation());

        policy.SeenCostUsd.Should().Be(60m);
    }

    [Fact]
    public async Task Sponsor_AlwaysPassesTargetAndSelectorToPolicy()
    {
        var policy = new StubPolicy(SponsorshipDecision.Deny(SponsorshipDenialReason.OverBudget, "stop before RPC"));

        await CreateSponsor(SigningOptions, policy).SponsorAsync(Operation());

        // The signer cannot be invoked without these, which is what closes the
        // wrong-target/wrong-selector hole left by HasSufficientSponsorshipQuotaAsync.
        policy.SeenTarget.Should().Be(Target);
        policy.SeenSelector.Should().Be(Selector);
    }

    [Fact]
    public async Task Sponsor_UnknownChain_RefusesToSign()
    {
        var policy = new StubPolicy(SponsorshipDecision.Approve(100m));

        var result = await CreateSponsor(SigningOptions, policy)
            .SponsorAsync(Operation() with { ChainKey = "ethereum-sepolia" });

        result.Approved.Should().BeFalse();
        result.Reason.Should().Be(SponsorshipDenialReason.NotConfigured);
    }

    [Fact]
    public async Task Sponsor_ZeroGas_PricesAtZero()
    {
        var policy = new StubPolicy(SponsorshipDecision.Deny(SponsorshipDenialReason.OverBudget, "stop before RPC"));

        await CreateSponsor(SigningOptions, policy).SponsorAsync(Operation(gas: 0));

        policy.SeenCostUsd.Should().Be(0m);
    }
}
