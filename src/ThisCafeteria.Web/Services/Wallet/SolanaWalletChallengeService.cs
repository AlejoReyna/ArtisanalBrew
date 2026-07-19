using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ThisCafeteria.Web.Services.Wallet;

public enum SolanaChallengeVerificationError { None, Expired, Mismatch, InvalidSignature }

public sealed record SolanaWalletChallenge(string Message, string Nonce, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

public interface ISolanaWalletChallengeService
{
    Task<SolanaWalletChallenge> CreateAsync(string publicKey, ChainDefinition chain, string origin, CancellationToken cancellationToken);
    Task<SolanaChallengeVerificationError> VerifyAsync(string publicKey, string message, string nonce, string chainKey, string origin, string signature, CancellationToken cancellationToken);
}

public sealed class SolanaWalletChallengeService(AppDbContext dbContext, ILogger<SolanaWalletChallengeService> logger) : ISolanaWalletChallengeService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public async Task<SolanaWalletChallenge> CreateAsync(string publicKey, ChainDefinition chain, string origin, CancellationToken cancellationToken)
    {
        if (!SolanaAddress.TryDecode(publicKey, out var keyBytes) || keyBytes.Length != 32) throw new ArgumentException("Invalid Solana public key.", nameof(publicKey));
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var issued = DateTimeOffset.UtcNow;
        var expires = issued.Add(Lifetime);
        var message = string.Join('\n',
            "ThisCafeteria Solana wallet login",
            string.Empty,
            $"Application: {origin}",
            $"URI: {origin}",
            $"Solana Public Key: {publicKey}",
            $"Chain Key: {chain.Key}",
            $"Cluster: {chain.SolanaCluster}",
            "Purpose: Authenticate wallet ownership for ThisCafeteria",
            "Version: 1",
            $"Nonce: {nonce}",
            $"Issued At: {issued:O}",
            $"Expiration Time: {expires:O}");
        var nonceHash = Hash(nonce);
        var messageHash = Hash(message);
        await dbContext.WalletAuthChallenges
            .Where(item => item.ExpiresAtUtc < issued.AddMinutes(-1) || item.ConsumedAtUtc != null && item.ConsumedAtUtc < issued.AddMinutes(-10))
            .OrderBy(item => item.ExpiresAtUtc)
            .Take(1000)
            .ExecuteDeleteAsync(cancellationToken);
        dbContext.WalletAuthChallenges.Add(new WalletAuthChallenge
        {
            NonceHash = nonceHash,
            PublicKey = publicKey,
            ChainKey = chain.Key,
            Origin = origin,
            MessageHash = messageHash,
            IssuedAtUtc = issued,
            ExpiresAtUtc = expires
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new SolanaWalletChallenge(message, nonce, issued, expires);
    }

    public async Task<SolanaChallengeVerificationError> VerifyAsync(string publicKey, string message, string nonce, string chainKey, string origin, string signature, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var challenge = await dbContext.WalletAuthChallenges.SingleOrDefaultAsync(item => item.NonceHash == Hash(nonce), cancellationToken);
        if (challenge is null || challenge.ConsumedAtUtc is not null || challenge.ExpiresAtUtc <= now) return SolanaChallengeVerificationError.Expired;
        if (!string.Equals(publicKey, challenge.PublicKey, StringComparison.Ordinal) ||
            !string.Equals(Hash(message), challenge.MessageHash, StringComparison.Ordinal) ||
            !string.Equals(chainKey, challenge.ChainKey, StringComparison.Ordinal) ||
            !string.Equals(origin, challenge.Origin, StringComparison.Ordinal)) return SolanaChallengeVerificationError.Mismatch;
        if (!SolanaAddress.TryDecode(signature, out var signatureBytes) || signatureBytes.Length != 64) return SolanaChallengeVerificationError.InvalidSignature;
        var verifier = new Ed25519Signer();
        if (!SolanaAddress.TryDecode(publicKey, out var publicKeyBytes) || publicKeyBytes.Length != 32) return SolanaChallengeVerificationError.Mismatch;
        verifier.Init(false, new Ed25519PublicKeyParameters(publicKeyBytes, 0));
        var messageBytes = Encoding.UTF8.GetBytes(message);
        verifier.BlockUpdate(messageBytes, 0, messageBytes.Length);
        if (!verifier.VerifySignature(signatureBytes)) return SolanaChallengeVerificationError.InvalidSignature;

        var consumed = await dbContext.WalletAuthChallenges
            .Where(item => item.NonceHash == Hash(nonce) && item.ConsumedAtUtc == null && item.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ConsumedAtUtc, now)
                .SetProperty(item => item.VerificationAttempts, item => item.VerificationAttempts + 1), cancellationToken);
        if (consumed != 1)
        {
            logger.LogWarning("Concurrent Solana wallet challenge consumption was rejected");
            return SolanaChallengeVerificationError.Expired;
        }
        return SolanaChallengeVerificationError.None;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal static class SolanaAddress
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    public static bool TryDecode(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value)) return false;
        var digits = new byte[(value.Length * 733 / 1000) + 1];
        var length = 1;
        foreach (var character in value)
        {
            var carry = Alphabet.IndexOf(character);
            if (carry < 0) return false;
            for (var index = 0; index < length; index++)
            {
                carry += 58 * digits[index];
                digits[index] = (byte)(carry % 256);
                carry /= 256;
            }
            while (carry > 0) { digits[length++] = (byte)(carry % 256); carry /= 256; }
        }
        while (length > 0 && digits[length - 1] == 0) length--;
        var leadingZeroes = value.TakeWhile(character => character == '1').Count();
        bytes = new byte[leadingZeroes + length];
        for (var index = 0; index < length; index++) bytes[bytes.Length - 1 - index] = digits[index];
        return bytes.Length is 32 or 64;
    }
}
