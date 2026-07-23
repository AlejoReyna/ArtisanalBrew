using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Application.Configuration;

namespace ThisCafeteria.Infrastructure.Services;

/// <summary>
/// Small transport adapter for an established ERC-4337 bundler (Rundler locally). It owns no
/// policy, keys, or chain addresses. The registry supplies both the endpoint and EntryPoint.
/// </summary>
public sealed class RundlerBundlerClient(HttpClient httpClient, IChainRegistry chains) : IBundlerClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private long _requestId;

    public async Task<string> SendUserOperationAsync(string chainKey, BundlerUserOperation operation, CancellationToken cancellationToken = default)
    {
        var chain = GetChain(chainKey);
        ValidateOperation(operation);
        var supported = await CallAsync<string[]>(chain, "eth_supportedEntryPoints", [], cancellationToken).ConfigureAwait(false);
        if (!supported.Any(address => string.Equals(address, chain.Deployment.EntryPoint, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Configured bundler does not advertise the trusted EntryPoint '{chain.Deployment.EntryPoint}'.");
        var packed = ToRpc(operation);
        var result = await CallAsync<string>(chain, "eth_sendUserOperation", [packed, chain.Deployment.EntryPoint], cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("Bundler returned an empty UserOperation hash.");
        return result;
    }

    public async Task<BundlerReceipt?> GetUserOperationReceiptAsync(string chainKey, string userOperationHash, CancellationToken cancellationToken = default)
    {
        var chain = GetChain(chainKey);
        if (!IsHash(userOperationHash)) throw new ArgumentException("A 32-byte UserOperation hash is required.", nameof(userOperationHash));
        var result = await CallAsync<JsonElement?>(chain, "eth_getUserOperationReceipt", [userOperationHash], cancellationToken).ConfigureAwait(false);
        return result is { ValueKind: JsonValueKind.Object } element ? ParseReceipt(element) : null;
    }

    // The ERC-4337 bundler-spec receipt nests the mined transaction's hash under `receipt`
    // (a standard transaction receipt) rather than at the top level, and uses `userOpHash`, not
    // `userOperationHash` — confirmed against Rundler's actual eth_getUserOperationReceipt
    // response, not assumed from BundlerReceipt's own field names. A flat-record Deserialize<T>
    // here would silently leave TransactionHash empty on a real bundler response.
    private static BundlerReceipt ParseReceipt(JsonElement element) => new()
    {
        UserOperationHash = GetString(element, "userOpHash"),
        TransactionHash = element.TryGetProperty("receipt", out var receipt) ? GetString(receipt, "transactionHash") : string.Empty,
        Sender = GetString(element, "sender"),
        Nonce = element.TryGetProperty("nonce", out var nonce) && nonce.ValueKind == JsonValueKind.String
            ? ParseHexQuantity(nonce.GetString())
            : BigInteger.Zero,
        Success = element.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True,
        RevertReason = element.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null
    };

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static BigInteger ParseHexQuantity(string? hex) =>
        !string.IsNullOrWhiteSpace(hex) && hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && BigInteger.TryParse("0" + hex[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : BigInteger.Zero;

    private async Task<T> CallAsync<T>(ChainDefinition chain, string method, object[] parameters, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(chain.BundlerRpcUrl, new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _requestId),
            method,
            @params = parameters
        }, JsonOptions, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"Bundler {method} failed: {error.GetRawText()}");
        if (!document.RootElement.TryGetProperty("result", out var result))
            throw new InvalidOperationException($"Bundler {method} returned no result.");
        return result.ValueKind == JsonValueKind.Null
            ? default!
            : result.Deserialize<T>(JsonOptions) ?? throw new InvalidOperationException($"Bundler {method} returned a null result.");
    }

    private ChainDefinition GetChain(string chainKey)
    {
        var chain = chains.All.FirstOrDefault(c => string.Equals(c.Key, chainKey, StringComparison.OrdinalIgnoreCase));
        if (chain is null || chain.Family != ChainFamily.Evm || string.IsNullOrWhiteSpace(chain.BundlerRpcUrl) || string.IsNullOrWhiteSpace(chain.Deployment.EntryPoint))
            throw new NotSupportedException($"ERC-4337 bundler submission is not configured for chain '{chainKey}'.");
        if (!Uri.TryCreate(chain.BundlerRpcUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"Bundler endpoint for '{chainKey}' is not an absolute HTTP(S) URL.");
        return chain;
    }

    private static void ValidateOperation(BundlerUserOperation operation)
    {
        if (!IsAddress(operation.Sender)) throw new ArgumentException("UserOperation sender must be an EVM address.", nameof(operation));
        if (!IsHex(operation.InitCode) || !IsHex(operation.CallData) || !IsHex(operation.AccountGasLimits) || !IsHex(operation.GasFees) || !IsHex(operation.PaymasterAndData) || !IsHex(operation.Signature))
            throw new ArgumentException("UserOperation byte fields must be 0x-prefixed hex.", nameof(operation));
        if (operation.AccountGasLimits.Length != 66 || operation.GasFees.Length != 66)
            throw new ArgumentException("v0.7 accountGasLimits and gasFees must each be bytes32.", nameof(operation));
        if (operation.InitCode.Length > 2 && operation.InitCode.Length < 42)
            throw new ArgumentException("initCode must be empty or contain a 20-byte factory followed by factoryData.", nameof(operation));
    }

    // Confirmed live against a real Rundler bundler (contracts/evm/scripts/crossstack-bundler-submit-check.ts):
    // eth_sendUserOperation rejects the on-chain packed accountGasLimits/gasFees bytes32 fields
    // outright ("data did not match any variant of untagged enum RpcUserOperation") - like
    // initCode's factory/factoryData split, v0.7's JSON-RPC schema splits these into separate
    // unpacked fields too; only the on-chain PackedUserOperation struct keeps them packed.
    private static object ToRpc(BundlerUserOperation operation)
    {
        var init = operation.InitCode.Length > 2 ? operation.InitCode : "0x";
        var (verificationGasLimit, callGasLimit) = UnpackHiLo(operation.AccountGasLimits);
        var (maxPriorityFeePerGas, maxFeePerGas) = UnpackHiLo(operation.GasFees);
        return new
        {
            sender = operation.Sender,
            nonce = HexQuantity(operation.Nonce),
            factory = init == "0x" ? null : $"0x{init[2..42]}",
            factoryData = init == "0x" ? null : $"0x{init[42..]}",
            callData = operation.CallData,
            callGasLimit = HexQuantity(callGasLimit),
            verificationGasLimit = HexQuantity(verificationGasLimit),
            preVerificationGas = HexQuantity(operation.PreVerificationGas),
            maxFeePerGas = HexQuantity(maxFeePerGas),
            maxPriorityFeePerGas = HexQuantity(maxPriorityFeePerGas),
            paymaster = operation.PaymasterAndData == "0x" ? null : $"0x{operation.PaymasterAndData[2..42]}",
            paymasterVerificationGasLimit = operation.PaymasterAndData == "0x" ? null : $"0x{operation.PaymasterAndData[42..74]}",
            paymasterPostOpGasLimit = operation.PaymasterAndData == "0x" ? null : $"0x{operation.PaymasterAndData[74..106]}",
            // paymasterData is everything AFTER paymaster+verificationGasLimit+postOpGasLimit (the
            // first 52 bytes / 106 hex chars past "0x") - for VerifyingPaymaster that's
            // abi.encode(validUntil, validAfter) (64 bytes) followed by the signature, not the
            // signature alone. Confirmed live: slicing off validUntil/validAfter here reconstructs
            // a corrupted paymasterAndData on Rundler's side and the paymaster's own
            // validatePaymasterUserOp reverts with AA33 (empty revert data - ecrecover on a
            // shifted/truncated signature).
            paymasterData = operation.PaymasterAndData == "0x" ? null : $"0x{operation.PaymasterAndData[106..]}",
            signature = operation.Signature
        };
    }

    /// <summary>Splits a packed bytes32 (hi 16 bytes | lo 16 bytes) the way v0.7 packs accountGasLimits and gasFees.</summary>
    private static (BigInteger hi, BigInteger lo) UnpackHiLo(string bytes32Hex)
    {
        var hex = bytes32Hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? bytes32Hex[2..] : bytes32Hex;
        var hi = BigInteger.Parse("0" + hex[..32], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var lo = BigInteger.Parse("0" + hex[32..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (hi, lo);
    }

    // BigInteger's own "x" format prepends a sign-disambiguation zero nibble whenever the most
    // significant nibble's high bit is set (e.g. 1_000_000 -> "0f4240", not "f4240") - harmless
    // numerically, but not the minimal-hex quantity encoding JSON-RPC "quantity" values are
    // supposed to use, and not what this project's own test literals (or a real bundler's strict
    // parser) expect.
    private static string HexQuantity(BigInteger value)
    {
        var hex = value.ToString("x").TrimStart('0');
        return hex.Length == 0 ? "0x0" : $"0x{hex}";
    }
    private static bool IsHash(string value) => IsHex(value) && value.Length == 66;
    private static bool IsAddress(string value) => IsHex(value) && value.Length == 42;
    private static bool IsHex(string? value) => !string.IsNullOrWhiteSpace(value) && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && value[2..].All(Uri.IsHexDigit);
}
