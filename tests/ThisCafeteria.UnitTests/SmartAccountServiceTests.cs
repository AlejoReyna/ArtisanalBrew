using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Infrastructure.Services;
using Xunit;

namespace ThisCafeteria.UnitTests;

public class SmartAccountServiceTests : IDisposable
{
    private const string ConfiguredChain = "evm-local";
    private const string NoFactoryChain = "evm-no-factory";
    private const string SolanaChain = "solana-local";
    private const string ModularChain = "evm-modular";
    private const string Owner = "0xf39fd6e51aad88f6f4ce6ab8827279cfffb92266";
    private const string Agent = "0x70997970c51812dc3a010c7d01b50e0d17dc79c8";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero));

    public SmartAccountServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

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

    /// <summary>A chain with the full, fail-closed-satisfying modular stack configured.</summary>
    private static ChainDefinition FullyConfiguredModularChain => new()
    {
        Key = ModularChain,
        Family = ChainFamily.Evm,
        EvmChainId = 31337,
        EvmChainIdHex = "0x7a69",
        PublicRpcUrl = "http://127.0.0.1:8545",
        Deployment = new ChainDeployment
        {
            EntryPoint = "0x8a791620dd6260079bf849dc5567adc3f2fdc318",
            ModularAccountFactory = "0x0b306bf915c4d645ff596e518faf3f9669b97016",
            DelegationManager = "0x5eb3bc0a489c5a8288765d2336659ebca68fcd00",
            HybridDeleGatorImplementation = "0x4c5859f0f772848b2d91f1d83e2fe57935348029",
            AllowedTargetsEnforcer = "0x9a9f2ccfde556a7e9ff0848998aa4a0cfd8863ae",
            AllowedMethodsEnforcer = "0x68b1d87f95878fe05b998f19b66f4baba5de1aed",
            ExactCalldataEnforcer = "0x99bba657f2bbc93c02d617f8ba121cb8fc104acf",
            LimitedCallsEnforcer = "0x322813fd9a801c5507c9de605d63cea4f2ce6c44",
            NonceEnforcer = "0xc3e53f4d16ae77db1c982e75a937b9f60fe63690",
            TimestampEnforcer = "0x59b670e9fa9d0a427751af201d676719a970857b"
        }
    };

    /// <summary>Same modular chain, but missing one enforcer address — must fail closed.</summary>
    private static ChainDefinition PartiallyConfiguredModularChain
    {
        get
        {
            var full = FullyConfiguredModularChain;
            return full with { Deployment = full.Deployment with { TimestampEnforcer = string.Empty } };
        }
    }

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

    private SmartAccountService CreateService(ISponsorshipPolicyService? sponsorship = null, params ChainDefinition[] extraChains) => new(
        new StubChainRegistry(
            [
                EvmChain(ConfiguredChain, "0x8a791620dd6260079bf849dc5567adc3f2fdc318", "0x1111111111111111111111111111111111111111"),
                EvmChain(NoFactoryChain, "0x8a791620dd6260079bf849dc5567adc3f2fdc318", string.Empty),
                new ChainDefinition { Key = SolanaChain, Family = ChainFamily.Solana, PublicRpcUrl = "http://127.0.0.1:8899" },
                .. extraChains
            ]),
        sponsorship ?? new StubSponsorshipPolicy(),
        _context,
        _time,
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

    [Fact]
    public async Task RevokeSessionPermissionsAsync_NoModularStackConfigured_OnlyRevokesSponsorship()
    {
        // ConfiguredChain has a legacy factory but no modular stack at all - must not attempt any
        // modular-account chain read, and must still revoke sponsorship.
        var policy = new StubSponsorshipPolicy();
        await CreateService(policy).RevokeSessionPermissionsAsync(ConfiguredChain, Owner);
        policy.RevokeCalled.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeSessionPermissionsAsync_ModularStackConfiguredButNoAccountRegistered_IsNoOp()
    {
        // A fully configured modular chain, but this owner has never registered a modular account.
        // Must return without attempting any on-chain read (which would fail - no RPC running here).
        var policy = new StubSponsorshipPolicy();
        var service = CreateService(policy, FullyConfiguredModularChain);

        await service.RevokeSessionPermissionsAsync(ModularChain, Owner);

        policy.RevokeCalled.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeSessionPermissionsAsync_AccountRegisteredButNoActiveEpoch_IsNoOp()
    {
        var policy = new StubSponsorshipPolicy();
        var service = CreateService(policy, FullyConfiguredModularChain);

        _context.SmartAccountRecords.Add(new SmartAccountRecord
        {
            ChainKey = ModularChain,
            OwnerAddress = Owner,
            AccountType = SmartAccountType.ModularHybridDeleGator,
            AccountAddress = "0x2222222222222222222222222222222222222222"
        });
        await _context.SaveChangesAsync();

        await service.RevokeSessionPermissionsAsync(ModularChain, Owner);

        policy.RevokeCalled.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterModularAccountAsync_UnconfiguredModularStack_ThrowsNotSupportedException()
    {
        // ConfiguredChain has a legacy factory but none of the modular addresses - fail closed.
        await FluentActions.Invoking(() => CreateService().RegisterModularAccountAsync(ConfiguredChain, Owner, "0x2222222222222222222222222222222222222222", "0"))
            .Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task RegisterModularAccountAsync_PartiallyConfiguredModularStack_ThrowsNotSupportedException()
    {
        var service = CreateService(extraChains: PartiallyConfiguredModularChain);
        await FluentActions.Invoking(() => service.RegisterModularAccountAsync(ModularChain, Owner, "0x2222222222222222222222222222222222222222", "0"))
            .Should().ThrowAsync<NotSupportedException>("every enforcer must be configured, not just some of them");
    }

    [Fact]
    public async Task RegisterModularAccountAsync_InvalidOwnerAddress_ThrowsBeforeAnyRpcCall()
    {
        var service = CreateService(extraChains: FullyConfiguredModularChain);
        await FluentActions.Invoking(() => service.RegisterModularAccountAsync(ModularChain, "0x123", "0x2222222222222222222222222222222222222222", "0"))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not a valid Ethereum address*");
    }

    [Fact]
    public async Task RegisterModularAccountAsync_InvalidAccountAddress_ThrowsBeforeAnyRpcCall()
    {
        var service = CreateService(extraChains: FullyConfiguredModularChain);
        await FluentActions.Invoking(() => service.RegisterModularAccountAsync(ModularChain, Owner, "0xnotanaddress", "0"))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not a valid Ethereum address*");
    }

    [Fact]
    public async Task RegisterModularAccountAsync_ConflictingAddressAlreadyRegistered_ThrowsBeforeAnyRpcCall()
    {
        var service = CreateService(extraChains: FullyConfiguredModularChain);
        _context.SmartAccountRecords.Add(new SmartAccountRecord
        {
            ChainKey = ModularChain,
            OwnerAddress = Owner,
            AccountType = SmartAccountType.ModularHybridDeleGator,
            AccountAddress = "0x3333333333333333333333333333333333333333"
        });
        await _context.SaveChangesAsync();

        await FluentActions.Invoking(() => service.RegisterModularAccountAsync(ModularChain, Owner, "0x2222222222222222222222222222222222222222", "0"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already has a different registered modular account*");
    }

    [Fact]
    public async Task GetActivePermissionEpochAsync_UnconfiguredModularStack_ReturnsNull()
    {
        var result = await CreateService().GetActivePermissionEpochAsync(ConfiguredChain, "0x2222222222222222222222222222222222222222");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActivePermissionEpochAsync_NoEpochRecorded_ReturnsNullWithoutAnyRpcCall()
    {
        var service = CreateService(extraChains: FullyConfiguredModularChain);
        var result = await service.GetActivePermissionEpochAsync(ModularChain, "0x2222222222222222222222222222222222222222");
        result.Should().BeNull();
    }

    [Fact]
    public async Task RecordPermissionEpochInstalledAsync_UnconfiguredModularStack_ThrowsNotSupportedException()
    {
        await FluentActions.Invoking(() => CreateService().RecordPermissionEpochInstalledAsync(
                ConfiguredChain, "0x2222222222222222222222222222222222222222", Agent, "1",
                _time.GetUtcNow().UtcDateTime, _time.GetUtcNow().UtcDateTime.AddHours(1), "0xtx",
                [new AgentPermissionGrantInput { TargetAddress = "0x4444444444444444444444444444444444444444", Selector = "0xb61d27f6", AmountWei = "1", DelegationHash = "0xabc", Description = "fund" }]))
            .Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task RecordPermissionEpochInstalledAsync_NoGrants_ThrowsArgumentExceptionBeforeAnyRpcCall()
    {
        var service = CreateService(extraChains: FullyConfiguredModularChain);
        await FluentActions.Invoking(() => service.RecordPermissionEpochInstalledAsync(
                ModularChain, "0x2222222222222222222222222222222222222222", Agent, "1",
                _time.GetUtcNow().UtcDateTime, _time.GetUtcNow().UtcDateTime.AddHours(1), "0xtx",
                []))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RecordPermissionEpochInstalledAsync_AccountNotRegistered_ThrowsInvalidOperationExceptionBeforeAnyRpcCall()
    {
        var service = CreateService(extraChains: FullyConfiguredModularChain);
        await FluentActions.Invoking(() => service.RecordPermissionEpochInstalledAsync(
                ModularChain, "0x2222222222222222222222222222222222222222", Agent, "1",
                _time.GetUtcNow().UtcDateTime, _time.GetUtcNow().UtcDateTime.AddHours(1), "0xtx",
                [new AgentPermissionGrantInput { TargetAddress = "0x4444444444444444444444444444444444444444", Selector = "0xb61d27f6", AmountWei = "1", DelegationHash = "0xabc", Description = "fund" }]))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No registered modular account*");
    }

    [Fact]
    public async Task DiscoverAccountsAsync_InvalidOwnerAddress_ThrowsArgumentException()
    {
        await FluentActions.Invoking(() => CreateService().DiscoverAccountsAsync(ConfiguredChain, "0x123"))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DiscoverAccountsAsync_UnconfiguredChains_ReturnsEmptyWithoutAnyRpcCall()
    {
        var result = await CreateService().DiscoverAccountsAsync("ethereum-sepolia", Owner);
        result.Should().BeEmpty();
    }
}
