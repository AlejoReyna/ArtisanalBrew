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
/// timeout never reach the bundler unnecessarily or debit the sponsorship grant. Confirmation is the
/// canonical EntryPoint event (behind <see cref="IEntryPointConfirmationReader"/>), never the
/// bundler's own receipt endpoint — so a mined operation is confirmed even when that endpoint errors
/// or times out. The reader's live-chain implementation (<see cref="EntryPointConfirmationReader"/>)
/// is exercised cross-stack instead, the same split <see cref="UserOperationSponsorTests"/> documents
/// for its own live-chain boundary; here it is stubbed so the orchestration itself is fully covered.
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
    private const string TxHash = "0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task MissingSignature_IsDeniedWithoutCallingTheBundler()
    {
        var bundler = new StubBundler(send: _ => throw new InvalidOperationException("must not be called"));
        var policy = new StubPolicy();
        var submitter = Build(bundler, new StubReader(), policy);

        var result = await submitter.SubmitAsync(Operation(), Approved(), signature: "");

        result.Status.Should().Be(UserOperationSubmissionStatus.Denied);
        policy.RecordedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task UnapprovedSponsorship_NeverReachesTheBundler()
    {
        var bundler = new StubBundler(send: _ => throw new InvalidOperationException("must not be called"));
        var policy = new StubPolicy();
        var submitter = Build(bundler, new StubReader(), policy);

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
        var submitter = Build(bundler, new StubReader(), policy);

        var result = await submitter.SubmitAsync(Operation(), Approved(), Signature);

        result.Status.Should().Be(UserOperationSubmissionStatus.Rejected);
        policy.RecordedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task NoConfirmationWithinThePollWindow_TimesOutWithoutRecordingUsage()
    {
        // The bundler never returns a receipt and the EntryPoint never yields a matching event; the
        // stub advances the fake clock past the poll deadline on its first receipt poll, so the
        // submitter's own deadline check fires without a real 60s wait.
        var bundler = new StubBundler(receipt: () =>
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            return null;
        });
        var policy = new StubPolicy();
        var submitter = Build(bundler, new StubReader(), policy);

        var result = await submitter.SubmitAsync(Operation(), Approved(), Signature);

        result.Status.Should().Be(UserOperationSubmissionStatus.TimedOut);
        result.UserOperationHash.Should().Be("0xhash");
        policy.RecordedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfirmedFromBundlerReceiptHint_RecordsUsageAndPassesTheHintThrough()
    {
        // Happy path with a healthy bundler receipt: the receipt's transaction hash is passed to the
        // reader as a hint, and the canonical EntryPoint event confirms success.
        var bundler = new StubBundler(receipt: () => new BundlerReceipt { TransactionHash = TxHash, Success = true });
        var reader = new StubReader(hint => new EntryPointConfirmation { TransactionHash = hint ?? TxHash, Success = true });
        var policy = new StubPolicy();
        var submitter = Build(bundler, reader, policy);

        var result = await submitter.SubmitAsync(Operation(), Approved(), Signature);

        result.Status.Should().Be(UserOperationSubmissionStatus.Confirmed);
        result.TransactionHash.Should().Be(TxHash);
        result.CostUsd.Should().Be(1.23m);
        reader.Hints.Should().ContainSingle().Which.Should().Be(TxHash);
        policy.RecordedRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task BundlerReceiptEndpointErrors_ButOperationIsMined_ConfirmsFromEntryPointEvent()
    {
        // The exact regression: self-hosted Rundler's eth_getUserOperationReceipt returns
        // "-32603 internal error: rpc provider error" AFTER the operation is mined. That must not
        // escape as an unhandled transport failure; the canonical EntryPoint event confirms it, and
        // the reader is asked with a null hint (there was no usable receipt).
        var bundler = new StubBundler(receipt: () =>
            throw new InvalidOperationException("Bundler eth_getUserOperationReceipt failed: {\"code\":-32603,\"message\":\"internal error: rpc provider error\"}"));
        var reader = new StubReader(_ => new EntryPointConfirmation { TransactionHash = TxHash, Success = true });
        var policy = new StubPolicy();
        var submitter = Build(bundler, reader, policy);

        var result = await submitter.SubmitAsync(Operation(), Approved(), Signature);

        result.Status.Should().Be(UserOperationSubmissionStatus.Confirmed);
        result.UserOperationHash.Should().Be("0xhash");
        result.TransactionHash.Should().Be(TxHash);
        reader.Hints.Should().ContainSingle().Which.Should().BeNull();
        policy.RecordedRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task BundlerReceiptEndpointErrors_AndNotMinedYet_TimesOutWithoutCrashingOrRecordingUsage()
    {
        // Same broken receipt endpoint, but the operation is genuinely not on-chain: the exception is
        // swallowed, the reader finds nothing, and the submitter fails closed with TimedOut rather
        // than propagating the transport error.
        var bundler = new StubBundler(receipt: () =>
        {
            _time.Advance(TimeSpan.FromMinutes(2));
            throw new InvalidOperationException("Bundler eth_getUserOperationReceipt failed: rpc provider error");
        });
        var policy = new StubPolicy();
        var submitter = Build(bundler, new StubReader(), policy);

        var result = await submitter.SubmitAsync(Operation(), Approved(), Signature);

        result.Status.Should().Be(UserOperationSubmissionStatus.TimedOut);
        result.UserOperationHash.Should().Be("0xhash");
        policy.RecordedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task MinedButInnerCallReverted_IsRevertedWithoutRecordingUsage()
    {
        var bundler = new StubBundler(receipt: () => new BundlerReceipt { TransactionHash = TxHash, Success = false });
        var reader = new StubReader(_ => new EntryPointConfirmation { TransactionHash = TxHash, Success = false });
        var policy = new StubPolicy();
        var submitter = Build(bundler, reader, policy);

        var result = await submitter.SubmitAsync(Operation(), Approved(), Signature);

        result.Status.Should().Be(UserOperationSubmissionStatus.Reverted);
        result.TransactionHash.Should().Be(TxHash);
        policy.RecordedRequests.Should().BeEmpty();
    }

    private UserOperationSubmitter Build(IBundlerClient bundler, IEntryPointConfirmationReader reader, ISponsorshipPolicyService policy) =>
        new(new StubChainRegistry(Chain()), bundler, reader, policy, _time, NullLogger<UserOperationSubmitter>.Instance);

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

    private sealed class StubReader(Func<string?, EntryPointConfirmation?>? find = null) : IEntryPointConfirmationReader
    {
        public List<string?> Hints { get; } = [];

        public Task<EntryPointConfirmation?> FindConfirmationAsync(string chainKey, string sender, string userOpHash, string? transactionHashHint, CancellationToken cancellationToken = default)
        {
            Hints.Add(transactionHashHint);
            return Task.FromResult(find?.Invoke(transactionHashHint));
        }
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
