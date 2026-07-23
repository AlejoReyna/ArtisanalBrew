using System.Numerics;
using Microsoft.Extensions.Logging;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Web3;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Infrastructure.Services;

/// <summary>
/// See <see cref="IUserOperationSubmitter"/>. Owns the submit -&gt; poll -&gt; independently-verify -&gt;
/// record-usage sequence; owns no policy or signing key of its own (that stays in
/// <see cref="IUserOperationSponsor"/> / <see cref="ISponsorshipPolicyService"/>) and never talks to
/// the EntryPoint except read-only, to fetch and decode the transaction the bundler says it mined.
/// </summary>
public sealed class UserOperationSubmitter(
    IChainRegistry chains,
    IBundlerClient bundler,
    ISponsorshipPolicyService policy,
    TimeProvider timeProvider,
    ILogger<UserOperationSubmitter> logger) : IUserOperationSubmitter
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(60);

    [Event("UserOperationEvent")]
    private sealed class UserOperationEventDto : IEventDTO
    {
        [Parameter("bytes32", "userOpHash", 1, true)] public byte[] UserOpHash { get; set; } = [];
        [Parameter("address", "sender", 2, true)] public string Sender { get; set; } = string.Empty;
        [Parameter("address", "paymaster", 3, true)] public string Paymaster { get; set; } = string.Empty;
        [Parameter("uint256", "nonce", 4, false)] public BigInteger Nonce { get; set; }
        [Parameter("bool", "success", 5, false)] public bool Success { get; set; }
        [Parameter("uint256", "actualGasCost", 6, false)] public BigInteger ActualGasCost { get; set; }
        [Parameter("uint256", "actualGasUsed", 7, false)] public BigInteger ActualGasUsed { get; set; }
    }

    public async Task<UserOperationSubmissionResult> SubmitAsync(SponsoredUserOperation operation, SponsorshipSignature sponsorship, string signature, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(signature) || !signature.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return UserOperationSubmissionResult.Denied("An owner signature is required.");

        if (!sponsorship.Approved)
        {
            logger.LogInformation(
                "Refusing to submit operation for {OwnerAddress} on {ChainKey}: {Reason}",
                operation.OwnerAddress, operation.ChainKey, sponsorship.Reason);
            return UserOperationSubmissionResult.Denied(sponsorship.Detail);
        }

        var chain = chains.GetRequired(operation.ChainKey);

        var bundlerOp = new BundlerUserOperation
        {
            Sender = operation.Sender,
            Nonce = operation.Nonce,
            InitCode = operation.InitCode,
            CallData = operation.CallData,
            AccountGasLimits = operation.AccountGasLimits,
            PreVerificationGas = operation.PreVerificationGas,
            GasFees = operation.GasFees,
            PaymasterAndData = sponsorship.PaymasterAndData,
            Signature = signature
        };

        string userOpHash;
        try
        {
            // Never EntryPoint.handleOps directly - the whole point of this class is that a real
            // bundler mines it, not that this process broadcasts a transaction itself.
            userOpHash = await bundler.SendUserOperationAsync(operation.ChainKey, bundlerOp, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            logger.LogWarning(exception, "Bundler rejected UserOperation for {OwnerAddress} on {ChainKey}", operation.OwnerAddress, operation.ChainKey);
            return new UserOperationSubmissionResult { Status = UserOperationSubmissionStatus.Rejected, Detail = exception.Message };
        }

        var receipt = await PollForReceiptAsync(operation.ChainKey, userOpHash, cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            return new UserOperationSubmissionResult
            {
                Status = UserOperationSubmissionStatus.TimedOut,
                Detail = "The bundler did not mine this operation within the poll window.",
                UserOperationHash = userOpHash
            };
        }

        // Never trust the bundler's own `success` flag alone: independently fetch the mined
        // transaction's real receipt and decode the canonical EntryPoint's own UserOperationEvent,
        // matching sender and userOpHash exactly - the same event-decode-and-match pattern this repo
        // already uses to verify liquid-staking and escrow transactions (see EvmLiquidStakingGateway).
        var web3 = new Web3(chain.EffectiveServerRpcUrl);
        var transactionReceipt = await web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(receipt.TransactionHash).ConfigureAwait(false);
        var eventLog = transactionReceipt?.DecodeAllEvents<UserOperationEventDto>().FirstOrDefault(item =>
            AddressMatches(item.Log.Address, chain.Deployment.EntryPoint) &&
            AddressMatches(item.Event.Sender, operation.Sender) &&
            ToHex(item.Event.UserOpHash).Equals(userOpHash, StringComparison.OrdinalIgnoreCase));

        if (eventLog is null)
        {
            return new UserOperationSubmissionResult
            {
                Status = UserOperationSubmissionStatus.Rejected,
                Detail = "The canonical EntryPoint did not emit a matching UserOperationEvent for this operation and sender.",
                UserOperationHash = userOpHash,
                TransactionHash = receipt.TransactionHash
            };
        }

        if (!eventLog.Event.Success)
        {
            return new UserOperationSubmissionResult
            {
                Status = UserOperationSubmissionStatus.Reverted,
                Detail = "The UserOperation was mined but its inner call reverted.",
                UserOperationHash = userOpHash,
                TransactionHash = receipt.TransactionHash
            };
        }

        // Only now - independently confirmed on-chain, not merely "a signature was produced" - does
        // spend actually get debited against the owner's grant.
        await policy.RecordUsageAsync(new SponsorshipRequest
        {
            ChainKey = operation.ChainKey,
            OwnerAddress = operation.OwnerAddress,
            EstimatedCostUsd = sponsorship.CostUsd,
            TargetAddress = operation.TargetAddress,
            Selector = operation.Selector
        }, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Confirmed sponsored UserOperation {UserOpHash} for {OwnerAddress} on {ChainKey}: tx {TransactionHash}, {CostUsd} USD.",
            userOpHash, operation.OwnerAddress, operation.ChainKey, receipt.TransactionHash, sponsorship.CostUsd);

        return new UserOperationSubmissionResult
        {
            Status = UserOperationSubmissionStatus.Confirmed,
            UserOperationHash = userOpHash,
            TransactionHash = receipt.TransactionHash,
            CostUsd = sponsorship.CostUsd
        };
    }

    private async Task<BundlerReceipt?> PollForReceiptAsync(string chainKey, string userOpHash, CancellationToken cancellationToken)
    {
        var deadline = timeProvider.GetUtcNow() + PollTimeout;
        while (true)
        {
            var receipt = await bundler.GetUserOperationReceiptAsync(chainKey, userOpHash, cancellationToken).ConfigureAwait(false);
            if (receipt is not null) return receipt;
            if (timeProvider.GetUtcNow() >= deadline) return null;
            await Task.Delay(PollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool AddressMatches(string? actual, string expected) =>
        !string.IsNullOrWhiteSpace(actual) && actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string ToHex(byte[] bytes) => "0x" + Convert.ToHexString(bytes).ToLowerInvariant();
}
