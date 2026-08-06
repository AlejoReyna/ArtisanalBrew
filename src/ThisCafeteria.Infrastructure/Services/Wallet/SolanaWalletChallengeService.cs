using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Application.Services.Wallet;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Services.Wallet;

public sealed class SolanaWalletChallengeService(
    IWalletAuthChallengeRepository challenges,
    ILogger<SolanaWalletChallengeService> logger) : ISolanaWalletChallengeService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const int Ed25519PublicKeyLength = 32;
    private const int Ed25519SignatureLength = 64;

    public async Task<SolanaWalletChallenge> CreateAsync(
        string publicKey,
        ChainDefinition chain,
        string origin,
        CancellationToken cancellationToken)
    {
        if (!SolanaBase58.TryDecode(publicKey, out var keyBytes) || keyBytes.Length != Ed25519PublicKeyLength)
        {
            throw new ArgumentException("Invalid Solana public key.", nameof(publicKey));
        }

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

        await challenges.PruneAsync(issued, cancellationToken: cancellationToken).ConfigureAwait(false);

        await challenges.AddAsync(
                new WalletAuthChallenge
                {
                    NonceHash = Hash(nonce),
                    PublicKey = publicKey,
                    ChainKey = chain.Key,
                    Origin = origin,
                    MessageHash = Hash(message),
                    IssuedAtUtc = issued,
                    ExpiresAtUtc = expires
                },
                cancellationToken)
            .ConfigureAwait(false);

        return new SolanaWalletChallenge(message, nonce, issued, expires);
    }

    public async Task<SolanaChallengeVerificationError> VerifyAsync(
        string publicKey,
        string message,
        string nonce,
        string chainKey,
        string origin,
        string signature,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var nonceHash = Hash(nonce);

        var challenge = await challenges.FindByNonceHashAsync(nonceHash, cancellationToken).ConfigureAwait(false);

        if (challenge is null || challenge.ConsumedAtUtc is not null || challenge.ExpiresAtUtc <= now)
        {
            return SolanaChallengeVerificationError.Expired;
        }

        // Every field the message committed to must still match, so a signature captured for one
        // origin or chain cannot be replayed against another.
        if (!string.Equals(publicKey, challenge.PublicKey, StringComparison.Ordinal) ||
            !string.Equals(Hash(message), challenge.MessageHash, StringComparison.Ordinal) ||
            !string.Equals(chainKey, challenge.ChainKey, StringComparison.Ordinal) ||
            !string.Equals(origin, challenge.Origin, StringComparison.Ordinal))
        {
            return SolanaChallengeVerificationError.Mismatch;
        }

        if (!SolanaBase58.TryDecode(signature, out var signatureBytes) || signatureBytes.Length != Ed25519SignatureLength)
        {
            return SolanaChallengeVerificationError.InvalidSignature;
        }

        if (!SolanaBase58.TryDecode(publicKey, out var publicKeyBytes) || publicKeyBytes.Length != Ed25519PublicKeyLength)
        {
            return SolanaChallengeVerificationError.Mismatch;
        }

        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(publicKeyBytes, 0));
        var messageBytes = Encoding.UTF8.GetBytes(message);
        verifier.BlockUpdate(messageBytes, 0, messageBytes.Length);

        if (!verifier.VerifySignature(signatureBytes))
        {
            return SolanaChallengeVerificationError.InvalidSignature;
        }

        // Consumed only after the signature checks out, and atomically - this is what stops the
        // same signature being redeemed twice by concurrent requests.
        if (!await challenges.TryConsumeAsync(nonceHash, now, cancellationToken).ConfigureAwait(false))
        {
            logger.LogWarning("Concurrent Solana wallet challenge consumption was rejected");
            return SolanaChallengeVerificationError.Expired;
        }

        return SolanaChallengeVerificationError.None;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
