namespace ThisCafeteria.Application.Services;

public interface ISmartAccountService
{
    /// <summary>
    /// Checks if a smart account implementation (e.g. factory, bundler, paymaster) is configured and available for the given chain.
    /// If false, other methods for this chain will throw NotSupportedException (fail-closed by design).
    /// </summary>
    Task<bool> IsConfiguredAsync(string chainKey);

    /// <summary>
    /// Gets an existing smart account address for the user on the specified chain, or deploys a new one if it doesn't exist.
    /// Throws NotSupportedException if no smart account infrastructure is configured for the chain.
    /// </summary>
    Task<string> GetOrDeployAccountAsync(string chainKey, string ownerAddress);

    /// <summary>
    /// Checks if the user's smart account has sufficient sponsorship quota remaining for the target operation.
    /// Returns false if unconfigured.
    /// </summary>
    Task<bool> HasSufficientSponsorshipQuotaAsync(string chainKey, string ownerAddress, decimal estimatedCostUsd);

    /// <summary>
    /// Records the usage of sponsorship credits against the user's account for a completed transaction.
    /// </summary>
    Task RecordSponsorshipUsageAsync(string chainKey, string ownerAddress, decimal costUsd);

    /// <summary>
    /// Revokes any active session-based permissions or delegated keys associated with the smart account.
    /// </summary>
    Task RevokeSessionPermissionsAsync(string chainKey, string ownerAddress);
}
