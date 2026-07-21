using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Services;
using Xunit;

namespace ThisCafeteria.UnitTests;

public class SmartAccountServiceTests
{
    private const string ConfiguredChain = "evm-local";
    private const string NoFactoryChain = "evm-no-factory";
    private const string SolanaChain = "solana-local";

    /// <summary>Minimal registry stub; ChainRegistry's own validation is exercised elsewhere.</summary>
    private sealed class StubChainRegistry(params ChainDefinition[] chains) : IChainRegistry
    {
        public string DefaultChainKey => chains.Length > 0 ? chains[0].Key : string.Empty;
        public IReadOnlyList<ChainDefinition> All { get; } = chains;

        public bool TryGet(string key, out ChainDefinition definition)
        {
            definition = All.FirstOrDefault(c => c.Key == key)!;
            return definition is not null;
        }

        public ChainDefinition GetRequired(string key) =>
            TryGet(key, out var d) ? d : throw new KeyNotFoundException(key);
    }

    private static ChainDefinition EvmChain(string key, string entryPoint, string accountFactory) => new()
    {
        Key = key,
        Family = ChainFamily.Evm,
        EvmChainId = 31337,
        EvmChainIdHex = "0x7a69",
        PublicRpcUrl = "http://127.0.0.1:8545",
        Deployment = new ChainDeployment { EntryPoint = entryPoint, AccountFactory = accountFactory }
    };

    /// <summary>
    /// Sponsorship policy stub. Denies by default, which is the fail-closed posture these tests
    /// assert; SponsorshipPolicyServiceTests covers the real policy behaviour.
    /// </summary>
    private sealed class StubSponsorshipPolicy(bool approve = false) : ISponsorshipPolicyService
    {
        public bool RevokeCalled { get; private set; }

        public Task<SponsorshipDecision> EvaluateAsync(SponsorshipRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(approve
                ? SponsorshipDecision.Approve(5m)
                : SponsorshipDecision.Deny(SponsorshipDenialReason.NotConfigured, "stub denies"));

        public Task RecordUsageAsync(SponsorshipRequest request, CancellationToken cancellationToken = default) =>
            approve ? Task.CompletedTask : throw new InvalidOperationException("Cannot record sponsorship usage: NotConfigured - stub denies");

        public Task RevokeAsync(string chainKey, string ownerAddress, CancellationToken cancellationToken = default)
        {
            RevokeCalled = true;
            return Task.CompletedTask;
        }
    }

    private static SmartAccountService CreateService(ISponsorshipPolicyService? sponsorship = null) => new(
        new StubChainRegistry(
            EvmChain(ConfiguredChain, "0x8a791620dd6260079bf849dc5567adc3f2fdc318", "0x1111111111111111111111111111111111111111"),
            EvmChain(NoFactoryChain, "0x8a791620dd6260079bf849dc5567adc3f2fdc318", string.Empty),
            new ChainDefinition { Key = SolanaChain, Family = ChainFamily.Solana, PublicRpcUrl = "http://127.0.0.1:8899" }),
        sponsorship ?? new StubSponsorshipPolicy(),
        NullLogger<SmartAccountService>.Instance);

    [Fact]
    public async Task IsConfiguredAsync_UnknownChain_ReturnsFalse()
    {
        var result = await CreateService().IsConfiguredAsync("ethereum-sepolia");
        result.Should().BeFalse("an unregistered chain has no ERC-4337 stack");
    }

    [Fact]
    public async Task IsConfiguredAsync_EvmChainWithEntryPointButNoFactory_ReturnsFalse()
    {
        var result = await CreateService().IsConfiguredAsync(NoFactoryChain);
        result.Should().BeFalse("an EntryPoint alone cannot derive or deploy accounts - fail closed");
    }

    [Fact]
    public async Task IsConfiguredAsync_NonEvmChain_ReturnsFalse()
    {
        var result = await CreateService().IsConfiguredAsync(SolanaChain);
        result.Should().BeFalse("ERC-4337 is EVM-only");
    }

    [Fact]
    public async Task IsConfiguredAsync_EvmChainWithEntryPointAndFactory_ReturnsTrue()
    {
        var result = await CreateService().IsConfiguredAsync(ConfiguredChain);
        result.Should().BeTrue("both the EntryPoint and the account factory are deployed");
    }

    [Fact]
    public async Task GetOrDeployAccountAsync_UnconfiguredChain_ThrowsNotSupportedException()
    {
        await FluentActions.Invoking(() => CreateService().GetOrDeployAccountAsync("ethereum-sepolia", "0x0000000000000000000000000000000000000001"))
            .Should().ThrowAsync<NotSupportedException>()
            .WithMessage("Smart account deployment is not configured for chain 'ethereum-sepolia'.");
    }

    [Fact]
    public async Task GetOrDeployAccountAsync_ChainMissingFactory_ThrowsNotSupportedException()
    {
        await FluentActions.Invoking(() => CreateService().GetOrDeployAccountAsync(NoFactoryChain, "0x0000000000000000000000000000000000000001"))
            .Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task GetOrDeployAccountAsync_InvalidOwnerAddress_ThrowsBeforeAnyRpcCall()
    {
        // The configured chain points at an RPC that is not running; reaching the network would
        // surface as a connection error instead. An ArgumentException proves validation comes first.
        await FluentActions.Invoking(() => CreateService().GetOrDeployAccountAsync(ConfiguredChain, "0x123"))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not a valid Ethereum address*");
    }

    [Fact]
    public async Task HasSufficientSponsorshipQuotaAsync_ReturnsFalse_WhenPolicyDenies()
    {
        var result = await CreateService().HasSufficientSponsorshipQuotaAsync(ConfiguredChain, "0x0000000000000000000000000000000000000001", 10.0m);
        result.Should().BeFalse("the policy denies, so the service must not report available quota");
    }

    [Fact]
    public async Task HasSufficientSponsorshipQuotaAsync_ReturnsTrue_WhenPolicyApproves()
    {
        var result = await CreateService(new StubSponsorshipPolicy(approve: true))
            .HasSufficientSponsorshipQuotaAsync(ConfiguredChain, "0x0000000000000000000000000000000000000001", 1.0m);
        result.Should().BeTrue("the policy approved the request");
    }

    [Fact]
    public async Task RecordSponsorshipUsageAsync_Throws_WhenPolicyWouldNotAuthorise()
    {
        await FluentActions.Invoking(() => CreateService().RecordSponsorshipUsageAsync(ConfiguredChain, "0x0000000000000000000000000000000000000001", 10.0m))
            .Should().ThrowAsync<InvalidOperationException>("usage must never be debited against a grant that would not authorise it");
    }

    [Fact]
    public async Task RevokeSessionPermissionsAsync_DelegatesToPolicy()
    {
        var policy = new StubSponsorshipPolicy();
        await CreateService(policy).RevokeSessionPermissionsAsync(ConfiguredChain, "0x0000000000000000000000000000000000000001");
        policy.RevokeCalled.Should().BeTrue("revocation must reach the sponsorship policy");
    }
}
