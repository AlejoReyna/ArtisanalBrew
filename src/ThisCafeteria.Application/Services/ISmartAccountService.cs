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
    /// Relays an already owner-signed UserOperation to the configured bundler, for the modular
    /// account already registered to <paramref name="ownerAddress"/> on <paramref name="chainKey"/>
    /// (via <see cref="RegisterModularAccountAsync"/>), then waits for it to be mined — it never
    /// signs, constructs, or re-derives anything, and never asserts that the operation activated or
    /// revoked a permission epoch, only that it was mined; <see cref="RecordPermissionEpochInstalledAsync"/>
    /// and <see cref="RevokeSessionPermissionsAsync"/> independently re-verify on-chain state
    /// afterward and remain the actual trust boundary — but they read that state live, so the caller
    /// must not call them until this method returns. Fails closed if the modular stack is not
    /// configured for this chain, or if <paramref name="operation"/>'s sender does not match the
    /// account already registered to this owner — this cannot be used as an open relay for an
    /// arbitrary account. Throws <see cref="InvalidOperationException"/> if the operation is mined
    /// but its inner call reverted, or if it is not confirmed within the poll window. Returns the
    /// real on-chain transaction hash once mined — not the bundler-assigned UserOperation hash.
    /// </summary>
    Task<string> SubmitOwnerUserOperationAsync(string chainKey, string ownerAddress, BundlerUserOperation operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience forward to the bundler's own <c>eth_estimateUserOperationGas</c>, resolved
    /// against <c>ChainDeployment.ModularEntryPoint</c> - never the legacy
    /// <c>ChainDeployment.EntryPoint</c>. This is not a trust boundary (it asserts nothing and
    /// persists nothing); the point is the same reason <see cref="IUserOperationSimulator"/> favors
    /// the EntryPoint's own simulation over a self-computed guess - a number this codebase invented
    /// would agree with itself and nothing else, whereas the bundler's estimate is what will actually
    /// be charged against. Fails closed with <see cref="NotSupportedException"/> if the modular stack
    /// is not configured for this chain.
    /// </summary>
    Task<BundlerGasEstimate> EstimateUserOperationGasAsync(string chainKey, BundlerUserOperation operation, CancellationToken cancellationToken = default);

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
