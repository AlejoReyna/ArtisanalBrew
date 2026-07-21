using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services;

/// <summary>Read-only view of a discovered/deployed smart account. See <see cref="SmartAccountRecord"/>.</summary>
public sealed record SmartAccountInfo
{
    public string ChainKey { get; init; } = string.Empty;
    public string OwnerAddress { get; init; } = string.Empty;
    public SmartAccountType AccountType { get; init; }
    public string AccountAddress { get; init; } = string.Empty;
    public string FactoryAddress { get; init; } = string.Empty;
    public bool IsDeployed { get; init; }
    public bool ImplementationVerified { get; init; }
}

/// <summary>One exact-calldata delegation to record as part of installing a permission epoch.</summary>
public sealed record AgentPermissionGrantInput
{
    public string TargetAddress { get; init; } = string.Empty;
    public string Selector { get; init; } = string.Empty;
    public string? TokenAddress { get; init; }
    public string AmountWei { get; init; } = "0";
    public string DelegationHash { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public sealed record AgentPermissionGrantInfo
{
    public string TargetAddress { get; init; } = string.Empty;
    public string Selector { get; init; } = string.Empty;
    public string? TokenAddress { get; init; }
    public string AmountWei { get; init; } = "0";
    public string DelegationHash { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

/// <summary>Read-only view of a permission epoch. See <see cref="AgentPermissionEpoch"/>.</summary>
public sealed record AgentPermissionEpochInfo
{
    public string ChainKey { get; init; } = string.Empty;
    public string DelegatorAddress { get; init; } = string.Empty;
    public string OwnerAddress { get; init; } = string.Empty;
    public string AgentAddress { get; init; } = string.Empty;
    public string Epoch { get; init; } = string.Empty;
    public DateTime ValidAfterUtc { get; init; }
    public DateTime ValidBeforeUtc { get; init; }
    public AgentPermissionEpochStatus Status { get; init; }
    public DateTime? InstalledAtUtc { get; init; }
    public string? InstalledTxHash { get; init; }
    public DateTime? RevokedAtUtc { get; init; }
    public string? RevokedTxHash { get; init; }
    public IReadOnlyList<AgentPermissionGrantInfo> Grants { get; init; } = Array.Empty<AgentPermissionGrantInfo>();
}

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
    /// This is the unchanged, legacy reference SimpleAccount path.
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
    /// For modular accounts this also revokes the owner's active <see cref="AgentPermissionEpoch"/>, but
    /// only after independently confirming on NonceEnforcer that the owner has actually advanced the
    /// on-chain epoch counter past it — this method never revokes anything the chain has not already revoked.
    /// </summary>
    Task RevokeSessionPermissionsAsync(string chainKey, string ownerAddress);

    /// <summary>
    /// Returns every known account (legacy SimpleAccount and modular HybridDeleGator) for this owner on
    /// this chain, refreshing deployment/implementation status against the chain first. An owner with no
    /// modular account registered yet gets only the legacy row (or an empty list on an unconfigured chain).
    /// </summary>
    Task<IReadOnlyList<SmartAccountInfo>> DiscoverAccountsAsync(string chainKey, string ownerAddress);

    /// <summary>
    /// Registers a modular HybridDeleGator account address for an owner. The address itself is supplied
    /// by the caller (computed via the audited @metamask/delegation-toolkit SDK, not re-derived here — see
    /// <see cref="SmartAccountRecord"/>). Verifies on-chain: if the account is already deployed, its
    /// ERC-1967 implementation slot must match the chain's configured HybridDeleGatorImplementation, or
    /// this throws. Fails closed with NotSupportedException if the modular stack is not configured for
    /// this chain.
    /// </summary>
    Task<SmartAccountInfo> RegisterModularAccountAsync(string chainKey, string ownerAddress, string accountAddress, string salt);

    /// <summary>
    /// Returns the currently active permission epoch for a modular account (delegator), if any, after
    /// confirming against a live NonceEnforcer.currentNonce read that the persisted epoch is still the
    /// one in effect on-chain. Returns null if there is no epoch, it was never activated, or it has been
    /// superseded/revoked on-chain (even if the local status flag is stale).
    /// </summary>
    Task<AgentPermissionEpochInfo?> GetActivePermissionEpochAsync(string chainKey, string delegatorAddress);

    /// <summary>
    /// Records that a permission epoch was installed. Verifies on-chain via NonceEnforcer.currentNonce
    /// that the epoch is actually active before persisting anything — this method cannot be used to
    /// assert a permission is live that the chain does not agree is live. Throws InvalidOperationException
    /// if the on-chain nonce does not equal the claimed epoch.
    /// </summary>
    Task<AgentPermissionEpochInfo> RecordPermissionEpochInstalledAsync(
        string chainKey,
        string delegatorAddress,
        string agentAddress,
        string epoch,
        DateTime validAfterUtc,
        DateTime validBeforeUtc,
        string installedTxHash,
        IReadOnlyList<AgentPermissionGrantInput> grants);
}
