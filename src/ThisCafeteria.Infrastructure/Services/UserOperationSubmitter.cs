using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Infrastructure.Services;

/// <summary>
/// See <see cref="IUserOperationSubmitter"/>. Owns the submit -&gt; poll -&gt; independently-verify -&gt;
/// record-usage sequence; owns no policy or signing key of its own (that stays in
/// <see cref="IUserOperationSponsor"/> / <see cref="ISponsorshipPolicyService"/>) and never talks to
/// the EntryPoint except read-only through <see cref="IEntryPointConfirmationReader"/>, to confirm the
/// transaction the bundler mined.
/// </summary>
public sealed class UserOperationSubmitter(
    IChainRegistry chains,
    IBundlerClient bundler,
    IEntryPointConfirmationReader confirmations,
    ISponsorshipPolicyService policy,
    TimeProvider timeProvider,
    ILogger<UserOperationSubmitter> logger) : IUserOperationSubmitter
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(60);

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

        _ = chains.GetRequired(operation.ChainKey);

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

        // Confirmation is always the canonical EntryPoint's own UserOperationEvent, never the
        // bundler's `success` flag. The bundler receipt is only a convenience hint for locating the
        // mined transaction; a broken or slow bundler receipt endpoint must not misreport an
        // already-mined operation as an unhandled transport failure (self-hosted Rundler's
        // eth_getUserOperationReceipt can return -32603 after the operation is on-chain).
        var confirmation = await PollForConfirmationAsync(operation, userOpHash, cancellationToken).ConfigureAwait(false);
        if (confirmation is null)
        {
            return new UserOperationSubmissionResult
            {
                Status = UserOperationSubmissionStatus.TimedOut,
                Detail = "The operation was not confirmed on-chain within the poll window.",
                UserOperationHash = userOpHash
            };
        }

        if (!confirmation.Success)
        {
            return new UserOperationSubmissionResult
            {
                Status = UserOperationSubmissionStatus.Reverted,
                Detail = "The UserOperation was mined but its inner call reverted.",
                UserOperationHash = userOpHash,
                TransactionHash = confirmation.TransactionHash
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
            userOpHash, operation.OwnerAddress, operation.ChainKey, confirmation.TransactionHash, sponsorship.CostUsd);

        return new UserOperationSubmissionResult
        {
            Status = UserOperationSubmissionStatus.Confirmed,
            UserOperationHash = userOpHash,
            TransactionHash = confirmation.TransactionHash,
            CostUsd = sponsorship.CostUsd
        };
    }

    private async Task<EntryPointConfirmation?> PollForConfirmationAsync(SponsoredUserOperation operation, string userOpHash, CancellationToken cancellationToken)
    {
        var deadline = timeProvider.GetUtcNow() + PollTimeout;
        while (true)
        {
            // The bundler receipt, when available, tells us exactly which transaction mined the op -
            // a fast hint. When its endpoint errors or times out, we swallow that (it is not
            // authoritative anyway) and confirm independently from the EntryPoint event below.
            var transactionHint = await TryGetTransactionHintAsync(operation.ChainKey, userOpHash, cancellationToken).ConfigureAwait(false);

            var confirmation = await TryConfirmAsync(operation, userOpHash, transactionHint, cancellationToken).ConfigureAwait(false);
            if (confirmation is not null) return confirmation;

            if (timeProvider.GetUtcNow() >= deadline) return null;
            await Task.Delay(PollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> TryGetTransactionHintAsync(string chainKey, string userOpHash, CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await bundler.GetUserOperationReceiptAsync(chainKey, userOpHash, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(receipt?.TransactionHash) ? null : receipt!.TransactionHash;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            logger.LogWarning(exception,
                "Bundler receipt endpoint unavailable for {UserOpHash} on {ChainKey}; confirming from the canonical EntryPoint event instead",
                userOpHash, chainKey);
            return null;
        }
    }

    private async Task<EntryPointConfirmation?> TryConfirmAsync(SponsoredUserOperation operation, string userOpHash, string? transactionHint, CancellationToken cancellationToken)
    {
        try
        {
            return await confirmations.FindConfirmationAsync(operation.ChainKey, operation.Sender, userOpHash, transactionHint, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            logger.LogWarning(exception,
                "On-chain confirmation lookup failed transiently for {UserOpHash} on {ChainKey}; will retry within the poll window",
                userOpHash, operation.ChainKey);
            return null;
        }
    }

    // Transport- or RPC-layer failures (including an HttpClient timeout, which surfaces as an
    // OperationCanceledException whose token is not the caller's) are transient: they must not escape
    // as an unhandled failure, because the operation may already be mined. A caller cancellation is
    // handled separately above and re-thrown.
    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or InvalidOperationException or JsonException or OperationCanceledException or TimeoutException;
}
