using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Infrastructure.Services;
using Xunit;

namespace ThisCafeteria.UnitTests;

/// <summary>
/// Fail-closed orchestration of <see cref="UserOperationSubmitter"/>: denial, bundler rejection, and
/// timeout never reach the bundler unnecessarily or debit the sponsorship grant. The "mined and
/// independently verified against the EntryPoint's own event log" happy path needs a live chain (the
/// verification step reads a real transaction receipt) and is proven cross-stack instead, the same
/// split UserOperationSponsorTests documents for its own live-chain boundary.
/// </summary>
public sealed class UserOperationSubmitterTests
{
    private const string ChainKey = "evm-local";
    private const string Owner = "0xf39fd6e51aad88f6f4ce6ab8827279cfffb92266";
    private const string Sender = "0x93e957812b6ce6e7100b0b743f39376838be992";
    private const string EntryPoint = "0x8a791620dd6260079bf849dc5567adc3f2fdc318";
    private const string Target = "0xa51c1fc2f0d1a1b8494ed1fe312d7c3a78ed91c0";
    private const string Selector = "0xb61d27f6";
    private const string Signature = "0xdeadbeef";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task MissingSignature_IsDeniedWithoutCallingTheBundler()
    {
        var bundler = new StubBundler(send: _ => throw new InvalidOperationException("must not be called"));
        var policy = new StubPolicy();
        var submitter = Build(bundler, policy);

        var result = await submitter.SubmitAsync(Operation(), Approved(), signature: "");

        result.Status.Should().Be(UserOperationSubmissionStatus.Denied);
        policy.RecordedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task UnapprovedSponsorship_NeverReachesTheBundler()
    {
        var bundler = new StubBundler(send: _ => throw new InvalidOperationException("must not be called"));
        var policy = new StubPolicy();
        var submitter = Build(bundler, policy);

        var result = await submitter.SubmitAsync(Operation(), SponsorshipSignature.Deny(SponsorshipDenialReason.DisallowedTarget, "target not allowed"), Signature);

        result.Status.Should().Be(UserOperationSubmissionStatus.Denied);
        result.Detail.Should().Be("target not allowed");
        policy.RecordedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task BundlerRejection_SurfacesAsRejectedWithoutRecordingUsage()
    {
        var bundler = new StubBundler(send: _ => throw new InvalidOperationException("Bundler does not support the trusted EntryPoint."));
        var policy = new StubPolicy();
        var submitter = Build(bundler, policy);

        var result = await submitter.SubmitAsync(Operation(), Approved(), Signature);

        result.Status.Should().Be(UserOperationSubmissionStatus.Rejected);
        policy.RecordedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task NoReceiptWithinThePollWindow_TimesOutWithoutRecordingUsage()
    {
        // The stub advances the fake clock past the poll deadline on its very first receipt poll,
        // so the submitter's own deadline check fires without needing a real 60s wait.
        var bundler = new StubBundler(receipt: () =>
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            return null;
        });
        var policy = new StubPolicy();
        var submitter = Build(bundler, policy);

        var result = await submitter.SubmitAsync(Operation(), Approved(), Signature);

        result.Status.Should().Be(UserOperationSubmissionStatus.TimedOut);
        result.UserOperationHash.Should().Be("0xhash");
        policy.RecordedRequests.Should().BeEmpty();
    }

    private UserOperationSubmitter Build(IBundlerClient bundler, ISponsorshipPolicyService policy) =>
        new(new StubChainRegistry(Chain()), bundler, policy, _time, NullLogger<UserOperationSubmitter>.Instance);

    private static SponsoredUserOperation Operation() => new()
    {
        ChainKey = ChainKey,
        OwnerAddress = Owner,
        Sender = Sender,
        Nonce = 0,
        InitCode = "0x",
        CallData = "0x12345678",
        AccountGasLimits = "0x" + new string('0', 64),
        PreVerificationGas = 100_000,
        GasFees = "0x" + new string('0', 64),
        TargetAddress = Target,
        Selector = Selector
    };

    private static SponsorshipSignature Approved() => new()
    {
        Approved = true,
        Reason = SponsorshipDenialReason.None,
        PaymasterAndData = "0xaabbcc",
        CostUsd = 1.23m
    };

    private static ChainDefinition Chain() => new()
    {
        Key = ChainKey,
        Family = ChainFamily.Evm,
        PublicRpcUrl = "http://127.0.0.1:8545",
        BundlerRpcUrl = "http://127.0.0.1:4338",
        Deployment = new ChainDeployment { EntryPoint = EntryPoint }
    };

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

    private sealed class StubBundler(Func<BundlerUserOperation, string>? send = null, Func<BundlerReceipt?>? receipt = null) : IBundlerClient
    {
        public Task<string> SendUserOperationAsync(string chainKey, BundlerUserOperation operation, CancellationToken cancellationToken = default) =>
            Task.FromResult(send is null ? "0xhash" : send(operation));

        public Task<BundlerReceipt?> GetUserOperationReceiptAsync(string chainKey, string userOperationHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(receipt is null ? null : receipt());
    }

    private sealed class StubPolicy : ISponsorshipPolicyService
    {
        public List<SponsorshipRequest> RecordedRequests { get; } = [];

        public Task<SponsorshipDecision> EvaluateAsync(SponsorshipRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(SponsorshipDecision.Approve(0m));

        public Task RecordUsageAsync(SponsorshipRequest request, CancellationToken cancellationToken = default)
        {
            RecordedRequests.Add(request);
            return Task.CompletedTask;
        }

        public Task RevokeAsync(string chainKey, string ownerAddress, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
