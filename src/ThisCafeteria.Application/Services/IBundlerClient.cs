using System.Numerics;

namespace ThisCafeteria.Application.Services;

/// <summary>ERC-4337 v0.7 operation in the bundler's JSON-RPC representation.</summary>
public sealed record BundlerUserOperation
{
    public required string Sender { get; init; }
    public BigInteger Nonce { get; init; }
    /// <summary>Packed on-chain initCode: factory address (20 bytes) followed by factory calldata.</summary>
    public string InitCode { get; init; } = "0x";
    public required string CallData { get; init; }
    public required string AccountGasLimits { get; init; }
    public BigInteger PreVerificationGas { get; init; }
    public required string GasFees { get; init; }
    public string PaymasterAndData { get; init; } = "0x";
    public required string Signature { get; init; }
}

public sealed record BundlerReceipt
{
    public string UserOperationHash { get; init; } = string.Empty;
    public string TransactionHash { get; init; } = string.Empty;
    public string Sender { get; init; } = string.Empty;
    public BigInteger Nonce { get; init; }
    public bool Success { get; init; }
    public string? RevertReason { get; init; }
}

/// <summary>
/// Result of <c>eth_estimateUserOperationGas</c>. <see cref="PaymasterVerificationGasLimit"/> and
/// <see cref="PaymasterPostOpGasLimit"/> are only populated by bundlers estimating a sponsored
/// operation; both are null for an unsponsored one.
/// </summary>
public sealed record BundlerGasEstimate
{
    public BigInteger PreVerificationGas { get; init; }
    public BigInteger VerificationGasLimit { get; init; }
    public BigInteger CallGasLimit { get; init; }
    public BigInteger? PaymasterVerificationGasLimit { get; init; }
    public BigInteger? PaymasterPostOpGasLimit { get; init; }
}

public interface IBundlerClient
{
    /// <summary>
    /// Submits through the configured bundler; never calls EntryPoint.handleOps directly.
    /// <paramref name="entryPointOverride"/> targets a specific EntryPoint deployment instead of the
    /// chain's default legacy <c>Deployment.EntryPoint</c> - required for a modular/HybridDeleGator
    /// account, whose immutable EntryPoint is the canonical singleton and may differ from the
    /// chain's legacy EntryPoint (see <c>ChainDeployment.ModularEntryPoint</c>). <paramref
    /// name="bundlerUrlOverride"/> targets a specific bundler endpoint instead of the chain's default
    /// <c>BundlerRpcUrl</c> - required whenever the legacy and canonical EntryPoints are served by two
    /// separate bundler instances rather than one instance supporting both (a single ERC-4337 bundler
    /// process is commonly configured for exactly one v0.7 EntryPoint address); see
    /// <c>ChainDefinition.EffectiveModularBundlerRpcUrl</c>.
    /// </summary>
    Task<string> SendUserOperationAsync(string chainKey, BundlerUserOperation operation, string? entryPointOverride = null, string? bundlerUrlOverride = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Wraps the standard ERC-4337 bundler RPC method <c>eth_estimateUserOperationGas</c>. Accepts a
    /// partially-formed <paramref name="operation"/> - bundlers do not validate the signature for
    /// this call, so a placeholder is fine - and returns real gas figures the bundler itself
    /// computed, rather than a guess this codebase would only agree with itself on. Same
    /// <paramref name="entryPointOverride"/>/<paramref name="bundlerUrlOverride"/> shape as
    /// <see cref="SendUserOperationAsync"/>.
    /// </summary>
    Task<BundlerGasEstimate> EstimateUserOperationGasAsync(string chainKey, BundlerUserOperation operation, string? entryPointOverride = null, string? bundlerUrlOverride = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// <paramref name="bundlerUrlOverride"/> must match whichever bundler endpoint the operation was
    /// originally submitted to via <see cref="SendUserOperationAsync"/> - a receipt lookup against the
    /// wrong bundler instance will simply never find it.
    /// </summary>
    Task<BundlerReceipt?> GetUserOperationReceiptAsync(string chainKey, string userOperationHash, string? bundlerUrlOverride = null, CancellationToken cancellationToken = default);
}
