using ThisCafeteria.Application.Configuration;
using Microsoft.Extensions.Logging;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Infrastructure.Services;

public class SmartAccountService(ILogger<SmartAccountService> logger) : ISmartAccountService
{
    public Task<bool> IsConfiguredAsync(string chainKey)
    {
        // Fail-closed: scaffolding, no factory/bundler configured.
        logger.LogDebug("IsConfiguredAsync checked for {ChainKey} - returning false (scaffolding)", chainKey);
        return Task.FromResult(false);
    }

    public Task<string> GetOrDeployAccountAsync(string chainKey, string ownerAddress)
    {
        logger.LogWarning("GetOrDeployAccountAsync called for {ChainKey} / {OwnerAddress} but no smart account infrastructure is configured.", chainKey, ownerAddress);
        throw new NotSupportedException($"Smart account deployment is not configured for chain '{chainKey}'.");
    }

    public Task<bool> HasSufficientSponsorshipQuotaAsync(string chainKey, string ownerAddress, decimal estimatedCostUsd)
    {
        logger.LogDebug("HasSufficientSponsorshipQuotaAsync returning false for {ChainKey} / {OwnerAddress}", chainKey, ownerAddress);
        return Task.FromResult(false);
    }

    public Task RecordSponsorshipUsageAsync(string chainKey, string ownerAddress, decimal costUsd)
    {
        logger.LogWarning("RecordSponsorshipUsageAsync called but sponsorship is not configured.");
        throw new NotSupportedException($"Sponsorship is not configured for chain '{chainKey}'.");
    }

    public Task RevokeSessionPermissionsAsync(string chainKey, string ownerAddress)
    {
        logger.LogWarning("RevokeSessionPermissionsAsync called but smart account sessions are not configured.");
        throw new NotSupportedException($"Smart account sessions are not configured for chain '{chainKey}'.");
    }
}
