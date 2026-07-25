using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.Infrastructure.Services;

/// <summary>
/// Fail-closed sponsorship policy. See <see cref="ISponsorshipPolicyService"/> for why this is the
/// real safety boundary rather than the paymaster contract.
/// </summary>
public sealed class SponsorshipPolicyService(
    AppDbContext db,
    IChainRegistry chains,
    SponsorshipPolicyOptions options,
    TimeProvider timeProvider,
    ILogger<SponsorshipPolicyService> logger) : ISponsorshipPolicyService
{
    public async Task<SponsorshipDecision> EvaluateAsync(SponsorshipRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.OwnerAddress) || request.EstimatedCostUsd < 0m)
        {
            return SponsorshipDecision.Deny(SponsorshipDenialReason.InvalidRequest, "Owner address is required and cost must not be negative.");
        }

        if (!options.Enabled)
        {
            return SponsorshipDecision.Deny(SponsorshipDenialReason.NotConfigured, "Sponsorship is disabled.");
        }

        if (!IsPaymasterConfigured(request.ChainKey))
        {
            return SponsorshipDecision.Deny(SponsorshipDenialReason.NotConfigured, $"Chain '{request.ChainKey}' has no configured paymaster.");
        }

        // Target/selector are only checked when supplied, so a coarse budget-only probe is still
        // possible. Callers that actually sign a sponsorship must supply both.
        if (request.TargetAddress is not null && !options.IsTargetAllowed(request.TargetAddress))
        {
            return SponsorshipDecision.Deny(SponsorshipDenialReason.DisallowedTarget, $"Target '{request.TargetAddress}' is not on the sponsorship allowlist.");
        }

        if (request.Selector is not null && !options.IsSelectorAllowed(request.Selector))
        {
            return SponsorshipDecision.Deny(SponsorshipDenialReason.DisallowedSelector, $"Selector '{request.Selector}' is not on the sponsorship allowlist.");
        }

        var grant = await FindGrantAsync(request.ChainKey, request.OwnerAddress, cancellationToken).ConfigureAwait(false);
        if (grant is null)
        {
            return SponsorshipDecision.Deny(SponsorshipDenialReason.NoGrant, $"No sponsorship grant for '{request.OwnerAddress}' on '{request.ChainKey}'.");
        }

        return EvaluateGrant(grant, request.EstimatedCostUsd);
    }

    public async Task RecordUsageAsync(SponsorshipRequest request, CancellationToken cancellationToken = default)
    {
        var decision = await EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!decision.Approved)
        {
            // Never debit against a grant that would not have authorised the operation.
            throw new InvalidOperationException($"Cannot record sponsorship usage: {decision.Reason} - {decision.Detail}");
        }

        var grant = await FindGrantAsync(request.ChainKey, request.OwnerAddress, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No sponsorship grant for '{request.OwnerAddress}' on '{request.ChainKey}'.");

        grant.SpentUsd += request.EstimatedCostUsd;
        grant.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        grant.ConcurrencyToken++;

        db.SponsorshipUsages.Add(new SponsorshipUsage
        {
            GrantId = grant.Id,
            ChainKey = grant.ChainKey,
            OwnerAddress = grant.OwnerAddress,
            CostUsd = request.EstimatedCostUsd,
            TargetAddress = Normalize(request.TargetAddress),
            Selector = Normalize(request.Selector),
            RecordedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Recorded {CostUsd} USD sponsorship for {OwnerAddress} on {ChainKey}; {RemainingUsd} USD remaining.",
            request.EstimatedCostUsd, grant.OwnerAddress, grant.ChainKey, grant.RemainingUsd);
    }

    public async Task RevokeAsync(string chainKey, string ownerAddress, CancellationToken cancellationToken = default)
    {
        var grant = await FindGrantAsync(chainKey, ownerAddress, cancellationToken).ConfigureAwait(false);
        if (grant is null)
        {
            logger.LogDebug("RevokeAsync: no grant for {OwnerAddress} on {ChainKey}; nothing to revoke.", ownerAddress, chainKey);
            return;
        }

        if (grant.RevokedAtUtc is not null)
        {
            return; // Already revoked; revocation is permanent and idempotent.
        }

        grant.RevokedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        grant.UpdatedAtUtc = grant.RevokedAtUtc.Value;
        grant.ConcurrencyToken++;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogWarning("Revoked sponsorship grant for {OwnerAddress} on {ChainKey}.", ownerAddress, chainKey);
    }

    public async Task<bool> RecordRevertedOperationAsync(string chainKey, string ownerAddress, CancellationToken cancellationToken = default)
    {
        var grant = await FindGrantAsync(chainKey, ownerAddress, cancellationToken).ConfigureAwait(false);
        if (grant is null || grant.RevokedAtUtc is not null)
        {
            // Nothing to meter. Called on a failure path, so this must stay quiet rather than
            // turning a reverted operation into a second, unrelated exception.
            return false;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        grant.RevertedOperationCount++;
        grant.UpdatedAtUtc = now;
        grant.ConcurrencyToken++;

        var shouldRevoke = options.MaxRevertedOperations > 0
            && grant.RevertedOperationCount >= options.MaxRevertedOperations;

        if (shouldRevoke)
        {
            grant.RevokedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (shouldRevoke)
        {
            logger.LogWarning(
                "Revoked sponsorship grant for {OwnerAddress} on {ChainKey}: {RevertedCount} sponsored operations reverted, reaching the limit of {MaxReverted}. Each cost the paymaster gas without debiting the grant's budget.",
                grant.OwnerAddress, grant.ChainKey, grant.RevertedOperationCount, options.MaxRevertedOperations);
        }
        else
        {
            logger.LogWarning(
                "Sponsored operation for {OwnerAddress} on {ChainKey} reverted after being mined; the paymaster paid but no budget was debited. {RevertedCount} of {MaxReverted} before this grant is revoked.",
                grant.OwnerAddress, grant.ChainKey, grant.RevertedOperationCount, options.MaxRevertedOperations);
        }

        return shouldRevoke;
    }

    private SponsorshipDecision EvaluateGrant(SponsorshipGrant grant, decimal estimatedCostUsd)
    {
        if (grant.RevokedAtUtc is not null)
        {
            return SponsorshipDecision.Deny(SponsorshipDenialReason.Revoked, $"Grant revoked at {grant.RevokedAtUtc:O}.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (now < grant.ValidFromUtc)
        {
            return SponsorshipDecision.Deny(SponsorshipDenialReason.NotYetValid, $"Grant is not valid until {grant.ValidFromUtc:O}.");
        }

        if (now >= grant.ValidUntilUtc)
        {
            return SponsorshipDecision.Deny(SponsorshipDenialReason.Expired, $"Grant expired at {grant.ValidUntilUtc:O}.");
        }

        if (grant.MaxOperationCostUsd > 0m && estimatedCostUsd > grant.MaxOperationCostUsd)
        {
            return SponsorshipDecision.Deny(SponsorshipDenialReason.OperationTooExpensive, $"Operation costs {estimatedCostUsd} USD, per-operation cap is {grant.MaxOperationCostUsd} USD.");
        }

        if (grant.SpentUsd + estimatedCostUsd > grant.BudgetUsd)
        {
            return SponsorshipDecision.Deny(SponsorshipDenialReason.OverBudget, $"Operation costs {estimatedCostUsd} USD but only {grant.RemainingUsd} USD remains.");
        }

        return SponsorshipDecision.Approve(grant.RemainingUsd - estimatedCostUsd);
    }

    private bool IsPaymasterConfigured(string chainKey)
    {
        var chain = chains.All.FirstOrDefault(c => string.Equals(c.Key, chainKey, StringComparison.OrdinalIgnoreCase));
        return chain is not null
            && chain.EvmChainId is not null
            && !string.IsNullOrWhiteSpace(chain.Deployment.EntryPoint)
            && !string.IsNullOrWhiteSpace(chain.Deployment.VerifyingPaymaster);
    }

    private Task<SponsorshipGrant?> FindGrantAsync(string chainKey, string ownerAddress, CancellationToken cancellationToken)
    {
        var owner = Normalize(ownerAddress);
        return db.SponsorshipGrants.FirstOrDefaultAsync(
            g => g.ChainKey == chainKey && g.OwnerAddress == owner,
            cancellationToken);
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
