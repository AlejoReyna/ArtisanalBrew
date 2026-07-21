using System.Numerics;

namespace ThisCafeteria.Application.Services;

/// <summary>A UserOperation shape to run through the canonical EntryPoint's gas simulation.</summary>
public sealed record UserOperationSimulationRequest
{
    public string ChainKey { get; init; } = string.Empty;
    public string Sender { get; init; } = string.Empty;
    public BigInteger Nonce { get; init; }
    public string InitCode { get; init; } = "0x";
    public string CallData { get; init; } = "0x";

    /// <summary>bytes32: verificationGasLimit (16 bytes) | callGasLimit (16 bytes).</summary>
    public string AccountGasLimits { get; init; } = string.Empty;

    public BigInteger PreVerificationGas { get; init; }

    /// <summary>bytes32: maxPriorityFeePerGas (16 bytes) | maxFeePerGas (16 bytes).</summary>
    public string GasFees { get; init; } = string.Empty;
}

public sealed record UserOperationSimulationResult
{
    public bool Success { get; init; }

    /// <summary>Populated when Success is false — a real validation failure (e.g. "AA21 didn't pay prefund"), not a signature mismatch.</summary>
    public string FailureReason { get; init; } = string.Empty;

    /// <summary>Gas the EntryPoint itself measured for validation + a placeholder execution.</summary>
    public BigInteger PreOpGas { get; init; }

    /// <summary>Wei the operation is projected to cost, as computed by the canonical EntryPoint — not derived from a caller-supplied gas figure.</summary>
    public BigInteger PaidWei { get; init; }

    public static UserOperationSimulationResult Failure(string reason) =>
        new() { Success = false, FailureReason = reason };
}

/// <summary>
/// Measures the real cost of a UserOperation by running it through the canonical EntryPoint's
/// <c>simulateHandleOp</c> via an <c>eth_call</c> state override — substituting the canonical
/// <c>EntryPointSimulations</c> bytecode for the real EntryPoint's code for the duration of one
/// read-only call. No transaction is broadcast, no chain state changes, and nothing is deployed:
/// the upstream contract's own constructor refuses if it is ever actually deployed.
///
/// The point of using the EntryPoint's own simulation rather than estimating gas independently is
/// the same reason <c>UserOperationSponsor</c> asks the paymaster for its own hash: a number this
/// codebase computed by itself would agree with itself and nothing else. The EntryPoint is the
/// thing that will actually charge for the operation, so it is the only trustworthy source for
/// what that charge will be.
/// </summary>
public interface IUserOperationSimulator
{
    Task<UserOperationSimulationResult> SimulateAsync(UserOperationSimulationRequest request, CancellationToken cancellationToken = default);
}
