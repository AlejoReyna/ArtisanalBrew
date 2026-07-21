using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.JsonRpc.Client;
using Nethereum.Web3;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Infrastructure.Services;

/// <summary>
/// See <see cref="IUserOperationSimulator"/> for what this proves and why. Implementation notes:
///
/// The signature used during simulation is a syntactically valid ECDSA signature over a fixed
/// placeholder message, signed by a fixed throwaway key baked into this class — never the real
/// account owner's key, which is not yet known at simulation time (the operation has not been
/// signed for real). It must be well-formed rather than empty: EntryPointSimulations "ignores
/// signature error" (a returned SIG_VALIDATION_FAILED is tolerated) but an empty or malformed
/// signature makes ECDSA recovery itself revert, which surfaces as "AA23 reverted" and aborts the
/// simulation before any gas figure is produced.
///
/// The sender's ETH balance is overridden to a large placeholder value for the same reason: a
/// freshly counterfactual account has no ETH and no EntryPoint deposit, so its own prefund payment
/// during validation would otherwise fail with "AA21 didn't pay prefund" before gas can be
/// measured. This has no effect on any real chain state — it exists only within the one eth_call.
/// </summary>
public sealed class UserOperationSimulator(
    IChainRegistry chains,
    ILogger<UserOperationSimulator> logger) : IUserOperationSimulator
{
    // A well-known throwaway key, used only to produce a syntactically valid placeholder
    // signature for gas simulation. It signs a fixed, meaningless message and controls nothing.
    private const string PlaceholderSignerKey = "0x59c6995e998f97a5a0044966f0945389dc9e86dae88c7a8412f4603b6b78690";
    private const string PlaceholderBalanceHex = "0x152d02c7e14af6800000"; // 100,000 ETH

    private static readonly Lazy<string> DeployedBytecode = new(LoadDeployedBytecode);

    [Struct("PackedUserOperation")]
    private sealed class PackedUserOperationDto
    {
        [Parameter("address", "sender", 1)] public string Sender { get; set; } = string.Empty;
        [Parameter("uint256", "nonce", 2)] public BigInteger Nonce { get; set; }
        [Parameter("bytes", "initCode", 3)] public byte[] InitCode { get; set; } = [];
        [Parameter("bytes", "callData", 4)] public byte[] CallData { get; set; } = [];
        [Parameter("bytes32", "accountGasLimits", 5)] public byte[] AccountGasLimits { get; set; } = new byte[32];
        [Parameter("uint256", "preVerificationGas", 6)] public BigInteger PreVerificationGas { get; set; }
        [Parameter("bytes32", "gasFees", 7)] public byte[] GasFees { get; set; } = new byte[32];
        [Parameter("bytes", "paymasterAndData", 8)] public byte[] PaymasterAndData { get; set; } = [];
        [Parameter("bytes", "signature", 9)] public byte[] Signature { get; set; } = [];
    }

    [Function("simulateHandleOp")]
    private sealed class SimulateHandleOpFunction : FunctionMessage
    {
        [Parameter("tuple", "op", 1)] public PackedUserOperationDto Op { get; set; } = new();
        [Parameter("address", "target", 2)] public string Target { get; set; } = "0x0000000000000000000000000000000000000000";
        [Parameter("bytes", "targetCallData", 3)] public byte[] TargetCallData { get; set; } = [];
    }

    // simulateHandleOp returns a single tuple value, not flat return fields, so decoding needs a
    // wrapper DTO with one "tuple"-typed parameter rather than a flat FunctionOutput.
    private sealed class ExecutionResultDto
    {
        [Parameter("uint256", "preOpGas", 1)] public BigInteger PreOpGas { get; set; }
        [Parameter("uint256", "paid", 2)] public BigInteger Paid { get; set; }
        [Parameter("uint256", "accountValidationData", 3)] public BigInteger AccountValidationData { get; set; }
        [Parameter("uint256", "paymasterValidationData", 4)] public BigInteger PaymasterValidationData { get; set; }
        [Parameter("bool", "targetSuccess", 5)] public bool TargetSuccess { get; set; }
        [Parameter("bytes", "targetResult", 6)] public byte[] TargetResult { get; set; } = [];
    }

    [FunctionOutput]
    private sealed class ExecutionResultWrapperDto : IFunctionOutputDTO
    {
        [Parameter("tuple", "", 1)] public ExecutionResultDto Result { get; set; } = new();
    }

    public async Task<UserOperationSimulationResult> SimulateAsync(UserOperationSimulationRequest request, CancellationToken cancellationToken = default)
    {
        var chain = chains.All.FirstOrDefault(c => string.Equals(c.Key, request.ChainKey, StringComparison.OrdinalIgnoreCase));
        if (chain is null || string.IsNullOrWhiteSpace(chain.Deployment.EntryPoint) || string.IsNullOrWhiteSpace(chain.EffectiveServerRpcUrl))
        {
            return UserOperationSimulationResult.Failure($"Chain '{request.ChainKey}' has no configured EntryPoint.");
        }

        var op = new PackedUserOperationDto
        {
            Sender = request.Sender,
            Nonce = request.Nonce,
            InitCode = HexToBytes(request.InitCode),
            CallData = HexToBytes(request.CallData),
            AccountGasLimits = HexToBytes32(request.AccountGasLimits),
            PreVerificationGas = request.PreVerificationGas,
            GasFees = HexToBytes32(request.GasFees),
            PaymasterAndData = [], // Base account cost only — see IUserOperationSimulator remarks.
            Signature = PlaceholderSignature()
        };

        var callData = new SimulateHandleOpFunction { Op = op }.GetCallData();
        var callObject = new { to = chain.Deployment.EntryPoint, data = "0x" + Convert.ToHexString(callData).ToLowerInvariant() };
        var stateOverride = new Dictionary<string, object>
        {
            [chain.Deployment.EntryPoint] = new Dictionary<string, object> { ["code"] = DeployedBytecode.Value },
            [request.Sender] = new Dictionary<string, object> { ["balance"] = PlaceholderBalanceHex }
        };

        var web3 = new Web3(chain.EffectiveServerRpcUrl);
        string raw;
        try
        {
            raw = await web3.Client.SendRequestAsync<string>("eth_call", null, callObject, "latest", stateOverride)
                .ConfigureAwait(false);
        }
        catch (RpcResponseException ex)
        {
            // A real validation failure — wrong nonce, expired window, paymaster rejects, etc. —
            // not a signature mismatch, which EntryPointSimulations tolerates rather than reverts.
            logger.LogInformation("UserOperation simulation reverted for {Sender} on {ChainKey}: {Message}", request.Sender, request.ChainKey, ex.Message);
            return UserOperationSimulationResult.Failure(ex.Message);
        }

        var wrapper = new FunctionCallDecoder().DecodeFunctionOutput<ExecutionResultWrapperDto>(raw);
        return new UserOperationSimulationResult
        {
            Success = true,
            PreOpGas = wrapper.Result.PreOpGas,
            PaidWei = wrapper.Result.Paid
        };
    }

    private static byte[] PlaceholderSignature()
    {
        var signer = new Nethereum.Signer.EthereumMessageSigner();
        var key = new Nethereum.Signer.EthECKey(PlaceholderSignerKey);
        var signatureHex = signer.Sign(System.Text.Encoding.UTF8.GetBytes("erc4337-gas-simulation"), key);
        return signatureHex.HexToByteArray();
    }

    private static string LoadDeployedBytecode()
    {
        var assembly = typeof(UserOperationSimulator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("EntryPointSimulations.generated.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' could not be opened.");
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.GetProperty("deployedBytecode").GetString()
            ?? throw new InvalidOperationException("EntryPointSimulations.generated.json is missing deployedBytecode.");
    }

    private static byte[] HexToBytes(string? hex) =>
        string.IsNullOrWhiteSpace(hex) || hex == "0x" ? [] : hex.HexToByteArray();

    private static byte[] HexToBytes32(string? hex)
    {
        var bytes = HexToBytes(hex);
        if (bytes.Length == 32) return bytes;
        var padded = new byte[32];
        Array.Copy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
        return padded;
    }
}
