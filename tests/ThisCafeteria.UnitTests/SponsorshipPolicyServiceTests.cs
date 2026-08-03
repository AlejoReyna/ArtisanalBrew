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

/// <summary>
/// Covers the Phase 4 gate requirement that over-budget, wrong-target, wrong-selector, expired,
/// and revoked sponsorship requests all fail — plus the fail-closed defaults around them.
/// </summary>
public class SponsorshipPolicyServiceTests : IDisposable
{
    private const string ChainKey = "evm-local";
    private const string Owner = "0xf39fd6e51aad88f6f4ce6ab8827279cfffb92266";
    private const string AllowedTarget = "0xa51c1fc2f0d1a1b8494ed1fe312d7c3a78ed91c0";
    private const string AllowedSelector = "0xb61d27f6";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero));

    public SponsorshipPolicyServiceTests()
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

    private static SponsorshipPolicyOptions EnabledOptions => new()
    {
        Enabled = true,
        AllowedTargets = new[] { AllowedTarget },
        AllowedSelectors = new[] { AllowedSelector }
    };

    private SponsorshipPolicyService CreateService(SponsorshipPolicyOptions? options = null) => new(
        _context,
        new StubChainRegistry(PaymasterChain),
        options ?? EnabledOptions,
        _time,
        NullLogger<SponsorshipPolicyService>.Instance);

    private async Task<SponsorshipGrant> SeedGrantAsync(
        decimal budget = 10m,
        decimal spent = 0m,
        decimal maxOperation = 5m,
        DateTime? validFrom = null,
        DateTime? validUntil = null,
        DateTime? revokedAt = null)
    {
        var grant = new SponsorshipGrant
        {
            ChainKey = ChainKey,
            OwnerAddress = Owner,
            BudgetUsd = budget,
            SpentUsd = spent,
            MaxOperationCostUsd = maxOperation,
            ValidFromUtc = validFrom ?? _time.GetUtcNow().UtcDateTime.AddDays(-1),
            ValidUntilUtc = validUntil ?? _time.GetUtcNow().UtcDateTime.AddDays(30),
            RevokedAtUtc = revokedAt
        };
        _context.SponsorshipGrants.Add(grant);
        await _context.SaveChangesAsync();
        return grant;
    }

    private static SponsorshipRequest Request(decimal cost = 1m, string? target = AllowedTarget, string? selector = AllowedSelector) => new()
    {
        ChainKey = ChainKey,
        OwnerAddress = Owner,
        EstimatedCostUsd = cost,
        TargetAddress = target,
        Selector = selector
    };

    // --- The five gate cases ---

    [Fact]
    public async Task Evaluate_OverBudget_IsDenied()
    {
        await SeedGrantAsync(budget: 10m, spent: 9.5m, maxOperation: 0m);

        var decision = await CreateService().EvaluateAsync(Request(cost: 1m));

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SponsorshipDenialReason.OverBudget);
    }

    [Fact]
    public async Task Evaluate_WrongTarget_IsDenied()
    {
        await SeedGrantAsync();

        var decision = await CreateService().EvaluateAsync(
            Request(target: "0xdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef"));

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SponsorshipDenialReason.DisallowedTarget);
    }

    [Fact]
    public async Task Evaluate_WrongSelector_IsDenied()
    {
        await SeedGrantAsync();

        var decision = await CreateService().EvaluateAsync(Request(selector: "0xdeadbeef"));

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SponsorshipDenialReason.DisallowedSelector);
    }

    [Fact]
    public async Task Evaluate_ExpiredGrant_IsDenied()
    {
        await SeedGrantAsync(validUntil: _time.GetUtcNow().UtcDateTime.AddMinutes(5));

        // Move past the grant's validity window.
        _time.Advance(TimeSpan.FromMinutes(10));

        var decision = await CreateService().EvaluateAsync(Request());

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SponsorshipDenialReason.Expired);
    }

    [Fact]
    public async Task Evaluate_RevokedGrant_IsDenied()
    {
        await SeedGrantAsync(revokedAt: _time.GetUtcNow().UtcDateTime.AddMinutes(-1));

        var decision = await CreateService().EvaluateAsync(Request());

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SponsorshipDenialReason.Revoked);
    }

    // --- Fail-closed defaults ---

    [Fact]
    public async Task Evaluate_SponsorshipDisabled_IsDenied()
    {
        await SeedGrantAsync();

        var decision = await CreateService(new SponsorshipPolicyOptions { Enabled = false })
            .EvaluateAsync(Request());

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SponsorshipDenialReason.NotConfigured);
    }

    [Fact]
    public async Task Evaluate_EmptyAllowlists_DenyEverything()
    {
        await SeedGrantAsync();

        // Enabled, but no targets or selectors configured: must deny, never allow-all.
        var decision = await CreateService(new SponsorshipPolicyOptions { Enabled = true })
            .EvaluateAsync(Request());

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SponsorshipDenialReason.DisallowedTarget);
    }

    [Fact]
    public async Task Evaluate_NoGrant_IsDenied()
    {
        var decision = await CreateService().EvaluateAsync(Request());

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SponsorshipDenialReason.NoGrant);
    }

    [Fact]
    public async Task Evaluate_NotYetValid_IsDenied()
    {
        await SeedGrantAsync(validFrom: _time.GetUtcNow().UtcDateTime.AddHours(1));

        var decision = await CreateService().EvaluateAsync(Request());

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SponsorshipDenialReason.NotYetValid);
    }

    [Fact]
    public async Task Evaluate_ExceedsPerOperationCap_IsDenied()
    {
        await SeedGrantAsync(budget: 100m, maxOperation: 1m);

        var decision = await CreateService().EvaluateAsync(Request(cost: 2m));

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SponsorshipDenialReason.OperationTooExpensive);
    }

    [Fact]
    public async Task Evaluate_ValidRequest_IsApprovedWithRemainingBudget()
    {
        await SeedGrantAsync(budget: 10m, spent: 2m);

        var decision = await CreateService().EvaluateAsync(Request(cost: 3m));

        decision.Approved.Should().BeTrue();
        decision.Reason.Should().Be(SponsorshipDenialReason.None);
        decision.RemainingUsd.Should().Be(5m, "10 budget - 2 already spent - 3 for this operation");
    }

    // --- Recording and revocation ---

    [Fact]
    public async Task RecordUsage_DebitsGrantAndWritesAuditRow()
    {
        await SeedGrantAsync(budget: 10m);

        await CreateService().RecordUsageAsync(Request(cost: 2.5m));

        var grant = await _context.SponsorshipGrants.SingleAsync();
        grant.SpentUsd.Should().Be(2.5m);

        var usage = await _context.SponsorshipUsages.SingleAsync();
        usage.GrantId.Should().Be(grant.Id);
        usage.CostUsd.Should().Be(2.5m);
        usage.TargetAddress.Should().Be(AllowedTarget);
        usage.Selector.Should().Be(AllowedSelector);
    }

    [Fact]
    public async Task RecordUsage_AccumulatesUntilBudgetIsExhausted()
    {
        await SeedGrantAsync(budget: 5m, maxOperation: 0m);
        var service = CreateService();

        await service.RecordUsageAsync(Request(cost: 3m));
        await service.RecordUsageAsync(Request(cost: 2m));

        // Budget is now fully consumed; the next operation must be refused.
        var decision = await service.EvaluateAsync(Request(cost: 0.01m));
        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SponsorshipDenialReason.OverBudget);
    }

    [Fact]
    public async Task RecordUsage_OnRevokedGrant_Throws()
    {
        await SeedGrantAsync(revokedAt: _time.GetUtcNow().UtcDateTime.AddMinutes(-1));

        await FluentActions.Invoking(() => CreateService().RecordUsageAsync(Request()))
            .Should().ThrowAsync<InvalidOperationException>("usage must never be debited against a revoked grant");

        (await _context.SponsorshipUsages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Revoke_MakesSubsequentRequestsFail_AndIsIdempotent()
    {
        await SeedGrantAsync();
        var service = CreateService();

        (await service.EvaluateAsync(Request())).Approved.Should().BeTrue("valid before revocation");

        await service.RevokeAsync(ChainKey, Owner);
        await service.RevokeAsync(ChainKey, Owner); // second call must not throw or change state

        var decision = await service.EvaluateAsync(Request());
        decision.Reason.Should().Be(SponsorshipDenialReason.Revoked);

        var grant = await _context.SponsorshipGrants.SingleAsync();
        grant.RevokedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Revoke_UnknownOwner_IsNoOp()
    {
        await FluentActions.Invoking(() => CreateService().RevokeAsync(ChainKey, "0x000000000000000000000000000000000000dead"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task Evaluate_OwnerAddressIsCaseInsensitive()
    {
        await SeedGrantAsync();

        var decision = await CreateService().EvaluateAsync(new SponsorshipRequest
        {
            ChainKey = ChainKey,
            OwnerAddress = Owner.ToUpperInvariant(),
            EstimatedCostUsd = 1m,
            TargetAddress = AllowedTarget.ToUpperInvariant(),
            Selector = AllowedSelector.ToUpperInvariant()
        });

        decision.Approved.Should().BeTrue("addresses and selectors must match case-insensitively");
    }

    [Fact]
    public async Task Evaluate_NegativeCost_IsRejectedAsInvalid()
    {
        await SeedGrantAsync();

        var decision = await CreateService().EvaluateAsync(Request(cost: -5m));

        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SponsorshipDenialReason.InvalidRequest);
    }

    // --- Reverted sponsored operations ---
    //
    // A mined-but-reverted operation costs the paymaster gas but has no successful operation to
    // price against the budget. Metering it is what stops a valid grant from draining the
    // paymaster's deposit for free; see SponsorshipGrant.RevertedOperationCount.

    [Fact]
    public async Task RecordRevertedOperation_CountsWithoutSpendingBudget()
    {
        var grant = await SeedGrantAsync(budget: 10m, spent: 2m);
        var options = EnabledOptions with { MaxRevertedOperations = 5 };

        var revoked = await CreateService(options).RecordRevertedOperationAsync(ChainKey, Owner);

        revoked.Should().BeFalse();
        var reloaded = await _context.SponsorshipGrants.FindAsync(grant.Id);
        reloaded!.RevertedOperationCount.Should().Be(1);
        reloaded.SpentUsd.Should().Be(2m, "a revert has no successful operation to price against the budget");
        reloaded.RevokedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task RecordRevertedOperation_RevokesGrantAtThreshold()
    {
        var grant = await SeedGrantAsync();
        var service = CreateService(EnabledOptions with { MaxRevertedOperations = 3 });

        (await service.RecordRevertedOperationAsync(ChainKey, Owner)).Should().BeFalse();
        (await service.RecordRevertedOperationAsync(ChainKey, Owner)).Should().BeFalse();
        (await service.RecordRevertedOperationAsync(ChainKey, Owner)).Should().BeTrue("the third revert reaches the limit");

        var reloaded = await _context.SponsorshipGrants.FindAsync(grant.Id);
        reloaded!.RevertedOperationCount.Should().Be(3);
        reloaded.RevokedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordRevertedOperation_RevocationActuallyStopsSponsorship()
    {
        await SeedGrantAsync();
        var service = CreateService(EnabledOptions with { MaxRevertedOperations = 1 });

        (await service.RecordRevertedOperationAsync(ChainKey, Owner)).Should().BeTrue();

        // The point of the counter is that it changes what gets sponsored next, not that a number
        // went up somewhere.
        var decision = await service.EvaluateAsync(Request());
        decision.Approved.Should().BeFalse();
        decision.Reason.Should().Be(SponsorshipDenialReason.Revoked);
    }

    [Fact]
    public async Task RecordRevertedOperation_ZeroLimitDisablesRevocation()
    {
        var grant = await SeedGrantAsync();
        var service = CreateService(EnabledOptions with { MaxRevertedOperations = 0 });

        for (var i = 0; i < 10; i++)
        {
            (await service.RecordRevertedOperationAsync(ChainKey, Owner)).Should().BeFalse();
        }

        var reloaded = await _context.SponsorshipGrants.FindAsync(grant.Id);
        reloaded!.RevertedOperationCount.Should().Be(10, "the count is still useful as a signal even when revocation is off");
        reloaded.RevokedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task RecordRevertedOperation_MissingOrRevokedGrant_DoesNotThrow()
    {
        var service = CreateService(EnabledOptions with { MaxRevertedOperations = 1 });

        // Called on a failure path: it must not turn a reverted operation into a second exception.
        (await service.RecordRevertedOperationAsync(ChainKey, Owner)).Should().BeFalse("no grant exists");

        await SeedGrantAsync(revokedAt: _time.GetUtcNow().UtcDateTime.AddMinutes(-5));
        (await service.RecordRevertedOperationAsync(ChainKey, Owner)).Should().BeFalse("an already-revoked grant cannot be revoked again");
    }
}
