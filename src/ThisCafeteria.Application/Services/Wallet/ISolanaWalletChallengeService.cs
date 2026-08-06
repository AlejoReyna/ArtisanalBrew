using ThisCafeteria.Application.Configuration;

namespace ThisCafeteria.Application.Services.Wallet;

public enum SolanaChallengeVerificationError { None, Expired, Mismatch, InvalidSignature }

public sealed record SolanaWalletChallenge(
    string Message,
    string Nonce,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Issues and verifies the sign-in message a Solana wallet signs to prove it controls a key.
/// </summary>
public interface ISolanaWalletChallengeService
{
    Task<SolanaWalletChallenge> CreateAsync(
        string publicKey,
        ChainDefinition chain,
        string origin,
        CancellationToken cancellationToken);

    Task<SolanaChallengeVerificationError> VerifyAsync(
        string publicKey,
        string message,
        string nonce,
        string chainKey,
        string origin,
        string signature,
        CancellationToken cancellationToken);
}
