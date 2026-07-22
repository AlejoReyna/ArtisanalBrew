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

public interface IBundlerClient
{
    /// <summary>Submits through the configured bundler; never calls EntryPoint.handleOps directly.</summary>
    Task<string> SendUserOperationAsync(string chainKey, BundlerUserOperation operation, CancellationToken cancellationToken = default);

    Task<BundlerReceipt?> GetUserOperationReceiptAsync(string chainKey, string userOperationHash, CancellationToken cancellationToken = default);
}
