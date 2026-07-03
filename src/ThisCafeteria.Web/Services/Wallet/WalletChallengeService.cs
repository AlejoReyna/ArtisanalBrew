using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Nethereum.Signer;
using Nethereum.Util;

namespace ThisCafeteria.Web.Services.Wallet;

public enum WalletChallengeVerificationError
{
    None,
    Expired,
    Mismatch,
    InvalidSignature
}

public sealed record WalletChallenge(string Message, string Nonce, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

public interface IWalletChallengeService
{
    WalletChallenge CreateChallenge(string address, int chainId, string networkName, string origin);

    WalletChallengeVerificationError Verify(
        string address,
        string message,
        string nonce,
        int chainId,
        string signature,
        out string recoveredAddress);
}

public sealed class WalletChallengeService(IMemoryCache cache) : IWalletChallengeService
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    public WalletChallenge CreateChallenge(string address, int chainId, string networkName, string origin)
    {
        var nonceBytes = RandomNumberGenerator.GetBytes(16);
        var nonce = Convert.ToHexString(nonceBytes).ToLowerInvariant();
        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.Add(ChallengeLifetime);
        var message = string.Join('\n',
            "ThisCafeteria wallet login",
            string.Empty,
            $"Address: {address}",
            $"Chain ID: {chainId.ToString(CultureInfo.InvariantCulture)}",
            $"Network: {networkName}",
            $"URI: {origin}",
            "Version: 1",
            $"Nonce: {nonce}",
            $"Issued At: {issuedAt:O}",
            $"Expiration Time: {expiresAt:O}");

        cache.Set(CacheKey(nonce), new CachedChallenge(address, message, chainId), expiresAt);
        return new WalletChallenge(message, nonce, issuedAt, expiresAt);
    }

    public WalletChallengeVerificationError Verify(
        string address,
        string message,
        string nonce,
        int chainId,
        string signature,
        out string recoveredAddress)
    {
        recoveredAddress = string.Empty;

        if (!cache.TryGetValue<CachedChallenge>(CacheKey(nonce), out var challenge) || challenge is null)
        {
            return WalletChallengeVerificationError.Expired;
        }

        cache.Remove(CacheKey(nonce));

        if (!AddressUtil.Current.AreAddressesTheSame(address, challenge.Address) ||
            message != challenge.Message ||
            chainId != challenge.ChainId)
        {
            return WalletChallengeVerificationError.Mismatch;
        }

        var signer = new EthereumMessageSigner();
        recoveredAddress = signer.EncodeUTF8AndEcRecover(message, signature);

        return AddressUtil.Current.AreAddressesTheSame(address, recoveredAddress)
            ? WalletChallengeVerificationError.None
            : WalletChallengeVerificationError.InvalidSignature;
    }

    private static string CacheKey(string nonce) => $"wallet-auth:{nonce}";

    private sealed record CachedChallenge(string Address, string Message, int ChainId);
}
