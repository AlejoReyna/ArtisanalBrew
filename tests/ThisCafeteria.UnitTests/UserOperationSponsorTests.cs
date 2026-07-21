using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Services;
using Xunit;

namespace ThisCafeteria.UnitTests;

/// <summary>
/// Fail-closed behaviour of the sponsorship signer. Every case here runs against a stub simulator,
/// so it exercises the sponsor's own logic (cost composition, policy gating, refusal ordering)
/// without a chain. The recipe the stub stands in for — eth_call state override against the
/// canonical EntryPoint — is proven for real by contracts/evm/scripts/simulation-recipe-check.ts,
/// and the full sponsor happy path (simulate -> policy -> sign -> on-chain accept) is proven
/// cross-stack by contracts/evm/scripts/crossstack-sponsor-check.ts, since a signature or a cost
/// figure that only this codebase agrees with would prove nothing.
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

    // GasFees packs maxPriorityFeePerGas (high 128 bits) | maxFeePerGas (low 128 bits) = 10 gwei.
    // Default PaymasterVerificationGasLimit (500,000) + PaymasterPostOpGasLimit (200,000) at that
    // price is 0.007 ETH of paymaster overhead, priced below alongside the simulated account cost.
    private const string GasFeesTenGwei = "0x0000000000000000000000003b9aca00000000000000000000000002540be400";
    private const decimal PaymasterOverheadEth = 0.007m; // 700,000 gas * 10 gwei

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

    private sealed class StubSimulator(UserOperationSimulationResult result) : IUserOperationSimulator
    {
        public UserOperationSimulationRequest? SeenRequest { get; private set; }

        public Task<UserOperationSimulationResult> SimulateAsync(UserOperationSimulationRequest request, CancellationToken cancellationToken = default)
        {
            SeenRequest = request;
            return Task.FromResult(result);
        }
    }

    private sealed class StubPolicy(SponsorshipDecision decision) : ISponsorshipPolicyService
    {
        public bool Invoked { get; private set; }
        public decimal? SeenCostUsd { get; private set; }
        public string? SeenTarget { get; private set; }
        public string? SeenSelector { get; private set; }

        public Task<SponsorshipDecision> EvaluateAsync(SponsorshipRequest request, CancellationToken cancellationToken = default)
        {
            Invoked = true;
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

    private static readonly UserOperationSimulationResult SuccessfulSimulation = new()
    {
        Success = true,
        PreOpGas = 300_000,
        PaidWei = 13_000_000_000_000_000 // 0.013 ETH: + 0.007 ETH paymaster overhead = 0.02 ETH = $60 @ $3000/ETH
    };

    private UserOperationSponsor CreateSponsor(
        SponsorshipPolicyOptions options,
        ISponsorshipPolicyService policy,
        IUserOperationSimulator? simulator = null) =>
        new(new StubChainRegistry(PaymasterChain), policy, simulator ?? new StubSimulator(SuccessfulSimulation),
            options, _time, NullLogger<UserOperationSponsor>.Instance);

    private static SponsoredUserOperation Operation() => new()
    {
        ChainKey = ChainKey,
        OwnerAddress = Owner,
        Sender = "0x93e957812b6ce6e7100b0b743f39376838be9920",
        Nonce = 0,
        InitCode = "0x",
        CallData = "0xb61d27f6",
        AccountGasLimits = "0x000000000000000000000000000f4240000000000000000000000000000f4240",
        PreVerificationGas = 100_000,
        GasFees = GasFeesTenGwei,
        TargetAddress = Target,
        Selector = Selector
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
    public async Task Sponsor_WhenSimulationFails_RefusesToSignWithoutConsultingPolicy()
    {
        var simulator = new StubSimulator(UserOperationSimulationResult.Failure("AA21 didn't pay prefund"));
        var policy = new StubPolicy(SponsorshipDecision.Approve(100m));

        var result = await CreateSponsor(SigningOptions, policy, simulator).SponsorAsync(Operation());

        result.Approved.Should().BeFalse();
        result.Reason.Should().Be(SponsorshipDenialReason.SimulationFailed);
        result.Detail.Should().Contain("AA21");
        result.PaymasterAndData.Should().BeEmpty();
        policy.Invoked.Should().BeFalse("an unsimulated operation has no trustworthy cost, so the policy must never be asked");
    }

    [Fact]
    public async Task Sponsor_PassesRawOperationFieldsToSimulator()
    {
        var simulator = new StubSimulator(SuccessfulSimulation);
        var policy = new StubPolicy(SponsorshipDecision.Deny(SponsorshipDenialReason.OverBudget, "stop before signing"));
        var operation = Operation();

        await CreateSponsor(SigningOptions, policy, simulator).SponsorAsync(operation);

        simulator.SeenRequest.Should().NotBeNull();
        simulator.SeenRequest!.Sender.Should().Be(operation.Sender);
        simulator.SeenRequest.ChainKey.Should().Be(operation.ChainKey);
        simulator.SeenRequest.GasFees.Should().Be(operation.GasFees);
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
    public async Task Sponsor_PricesFromSimulationPlusPaymasterOverhead_RatherThanTrustingCaller()
    {
        var policy = new StubPolicy(SponsorshipDecision.Deny(SponsorshipDenialReason.OverBudget, "stop before RPC"));

        // Simulated 0.013 ETH + 0.007 ETH paymaster overhead = 0.02 ETH = 60 USD at 3000 USD/ETH.
        await CreateSponsor(SigningOptions, policy).SponsorAsync(Operation());

        policy.SeenCostUsd.Should().Be(60m);
    }

    [Fact]
    public async Task Sponsor_ZeroSimulatedCost_StillPricesPaymasterOverhead()
    {
        var simulator = new StubSimulator(SuccessfulSimulation with { PaidWei = 0 });
        var policy = new StubPolicy(SponsorshipDecision.Deny(SponsorshipDenialReason.OverBudget, "stop before RPC"));

        // Only the paymaster's own overhead remains: 0.007 ETH * 3000 USD/ETH.
        await CreateSponsor(SigningOptions, policy, simulator).SponsorAsync(Operation());

        policy.SeenCostUsd.Should().Be(PaymasterOverheadEth * 3000m);
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
    public async Task Sponsor_UnknownChain_RefusesToSignWithoutSimulating()
    {
        var simulator = new StubSimulator(SuccessfulSimulation);
        var policy = new StubPolicy(SponsorshipDecision.Approve(100m));

        var result = await CreateSponsor(SigningOptions, policy, simulator)
            .SponsorAsync(Operation() with { ChainKey = "ethereum-sepolia" });

        result.Approved.Should().BeFalse();
        result.Reason.Should().Be(SponsorshipDenialReason.NotConfigured);
        simulator.SeenRequest.Should().BeNull("an unconfigured chain must be rejected before simulation is attempted");
    }
}
