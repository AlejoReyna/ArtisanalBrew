using System.Numerics;
using Microsoft.Extensions.Logging;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Web3;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Infrastructure.Services;

/// <summary>
/// Produces canonical VerifyingPaymaster sponsorship signatures for policy-approved operations.
///
/// The hash that gets signed is obtained from the paymaster contract itself
/// (<c>getHash(userOp, validUntil, validAfter)</c>) rather than reimplemented here. Reimplementing
/// v0.7 hashing would produce something that agrees with itself and nothing else; asking the
/// contract means a mismatch surfaces as a rejected signature instead of a silent divergence.
/// </summary>
public sealed class UserOperationSponsor(
    IChainRegistry chains,
    ISponsorshipPolicyService policy,
    SponsorshipPolicyOptions options,
    TimeProvider timeProvider,
    ILogger<UserOperationSponsor> logger) : IUserOperationSponsor
{
    private const decimal WeiPerEther = 1_000_000_000_000_000_000m;

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

    [Function("getHash", "bytes32")]
    private sealed class GetHashFunction : FunctionMessage
    {
        [Parameter("tuple", "userOp", 1)]
        public PackedUserOperationDto UserOp { get; set; } = new();

        [Parameter("uint48", "validUntil", 2)] public BigInteger ValidUntil { get; set; }
        [Parameter("uint48", "validAfter", 3)] public BigInteger ValidAfter { get; set; }
    }

    public async Task<SponsorshipSignature> SponsorAsync(SponsoredUserOperation operation, CancellationToken cancellationToken = default)
    {
        if (!options.CanSign)
        {
            // No signer key, or gas cannot be priced. Refuse rather than sign or treat gas as free.
            logger.LogDebug("Sponsorship signing is not configured; refusing to sign.");
            return SponsorshipSignature.Deny(SponsorshipDenialReason.NotConfigured, "Sponsorship signing is not configured.");
        }

        var chain = chains.All.FirstOrDefault(c => string.Equals(c.Key, operation.ChainKey, StringComparison.OrdinalIgnoreCase));
        if (chain is null || string.IsNullOrWhiteSpace(chain.Deployment.VerifyingPaymaster))
        {
            return SponsorshipSignature.Deny(SponsorshipDenialReason.NotConfigured, $"Chain '{operation.ChainKey}' has no configured paymaster.");
        }

        // Cost is derived from gas, never taken from the caller: a budget enforced against a
        // self-reported number is advisory, not a control.
        var costUsd = EstimateCostUsd(operation.EstimatedGas, operation.GasPriceWei);

        var decision = await policy.EvaluateAsync(new SponsorshipRequest
        {
            ChainKey = operation.ChainKey,
            OwnerAddress = operation.OwnerAddress,
            EstimatedCostUsd = costUsd,
            TargetAddress = operation.TargetAddress,
            Selector = operation.Selector
        }, cancellationToken).ConfigureAwait(false);

        if (!decision.Approved)
        {
            logger.LogInformation(
                "Refusing to sponsor operation for {OwnerAddress} on {ChainKey}: {Reason}",
                operation.OwnerAddress, operation.ChainKey, decision.Reason);
            return SponsorshipSignature.Deny(decision.Reason, decision.Detail);
        }

        var now = timeProvider.GetUtcNow();
        var validUntil = (BigInteger)now.Add(options.SignatureValidity).ToUnixTimeSeconds();
        var validAfter = BigInteger.Zero;

        var paymaster = chain.Deployment.VerifyingPaymaster;
        var stub = BuildPaymasterAndData(paymaster, validUntil, validAfter, signature: null);

        var dto = new PackedUserOperationDto
        {
            Sender = operation.Sender,
            Nonce = operation.Nonce,
            InitCode = HexToBytes(operation.InitCode),
            CallData = HexToBytes(operation.CallData),
            AccountGasLimits = HexToBytes32(operation.AccountGasLimits),
            PreVerificationGas = operation.PreVerificationGas,
            GasFees = HexToBytes32(operation.GasFees),
            PaymasterAndData = HexToBytes(stub),
            Signature = []
        };

        var web3 = new Web3(chain.EffectiveServerRpcUrl);
        var handler = web3.Eth.GetContractQueryHandler<GetHashFunction>();
        var hash = await handler.QueryAsync<byte[]>(paymaster, new GetHashFunction
        {
            UserOp = dto,
            ValidUntil = validUntil,
            ValidAfter = validAfter
        }).ConfigureAwait(false);

        // VerifyingPaymaster verifies an EIP-191 personal_sign over the hash.
        var signer = new EthereumMessageSigner();
        var signature = signer.Sign(hash, new EthECKey(options.VerifyingSignerPrivateKey));

        logger.LogInformation(
            "Sponsoring operation for {OwnerAddress} on {ChainKey}: {CostUsd} USD, valid until {ValidUntil}.",
            operation.OwnerAddress, operation.ChainKey, costUsd, validUntil);

        return new SponsorshipSignature
        {
            Approved = true,
            Reason = SponsorshipDenialReason.None,
            PaymasterAndData = BuildPaymasterAndData(paymaster, validUntil, validAfter, signature),
            CostUsd = costUsd
        };
    }

    /// <summary>gas * gasPrice (wei) -> ether -> USD.</summary>
    private decimal EstimateCostUsd(BigInteger estimatedGas, BigInteger gasPriceWei)
    {
        if (estimatedGas <= BigInteger.Zero || gasPriceWei <= BigInteger.Zero) return 0m;
        var weiCost = estimatedGas * gasPriceWei;
        return (decimal)weiCost / WeiPerEther * options.NativeCurrencyUsdRate;
    }

    /// <summary>
    /// v0.7 layout: paymaster(20) | verificationGasLimit(16) | postOpGasLimit(16) |
    /// abi.encode(validUntil, validAfter) (64) | signature.
    /// </summary>
    private string BuildPaymasterAndData(string paymaster, BigInteger validUntil, BigInteger validAfter, string? signature)
    {
        var sb = new System.Text.StringBuilder("0x");
        sb.Append(paymaster.RemoveHexPrefix());
        sb.Append(ToPaddedHex(options.PaymasterVerificationGasLimit, 16));
        sb.Append(ToPaddedHex(options.PaymasterPostOpGasLimit, 16));
        sb.Append(ToPaddedHex(validUntil, 32));
        sb.Append(ToPaddedHex(validAfter, 32));
        if (!string.IsNullOrEmpty(signature)) sb.Append(signature.RemoveHexPrefix());
        return sb.ToString();
    }

    private static string ToPaddedHex(BigInteger value, int byteLength)
    {
        var hex = value.ToString("x").TrimStart('0');
        if (hex.Length == 0) hex = "0";
        return hex.PadLeft(byteLength * 2, '0');
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
