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

    public Task<BundlerReceipt?> GetUserOperationReceiptAsync(string chainKey, string userOperationHash, CancellationToken cancellationToken = default)
    {
        var chain = GetChain(chainKey);
        if (!IsHash(userOperationHash)) throw new ArgumentException("A 32-byte UserOperation hash is required.", nameof(userOperationHash));
        return CallAsync<BundlerReceipt?>(chain, "eth_getUserOperationReceipt", [userOperationHash], cancellationToken);
    }

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

    private static object ToRpc(BundlerUserOperation operation)
    {
        var init = operation.InitCode.Length > 2 ? operation.InitCode : "0x";
        return new
        {
            sender = operation.Sender,
            nonce = HexQuantity(operation.Nonce),
            factory = init == "0x" ? null : $"0x{init[2..42]}",
            factoryData = init == "0x" ? null : $"0x{init[42..]}",
            callData = operation.CallData,
            accountGasLimits = operation.AccountGasLimits,
            preVerificationGas = HexQuantity(operation.PreVerificationGas),
            gasFees = operation.GasFees,
            paymaster = operation.PaymasterAndData == "0x" ? null : $"0x{operation.PaymasterAndData[2..42]}",
            paymasterVerificationGasLimit = operation.PaymasterAndData == "0x" ? null : $"0x{operation.PaymasterAndData[42..74]}",
            paymasterPostOpGasLimit = operation.PaymasterAndData == "0x" ? null : $"0x{operation.PaymasterAndData[74..106]}",
            // paymaster(20) + verificationGasLimit(16) + postOpGasLimit(16) + validUntil(32)
            // + validAfter(32) = 116 bytes / 232 hex characters, plus the 0x prefix.
            paymasterData = operation.PaymasterAndData == "0x" ? null : $"0x{operation.PaymasterAndData[234..]}",
            signature = operation.Signature
        };
    }

    private static string HexQuantity(BigInteger value) => $"0x{value.ToString("x")}";
    private static bool IsHash(string value) => IsHex(value) && value.Length == 66;
    private static bool IsAddress(string value) => IsHex(value) && value.Length == 42;
    private static bool IsHex(string? value) => !string.IsNullOrWhiteSpace(value) && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && value[2..].All(Uri.IsHexDigit);
}
