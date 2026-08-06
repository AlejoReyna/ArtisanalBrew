using ThisCafeteria.Application.Services.Blockchain;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;

namespace ThisCafeteria.Infrastructure.Services.Blockchain;

/// <summary>
/// Loads and validates the Solana faucet mint-authority secret from the
/// <c>ARTISANALBREW_SOLANA_ADMIN_KEY</c> environment variable. Fails closed: the derived public key
/// must equal the manifest administrator, or the faucet refuses to operate. Accepts the Solana CLI
/// keypair format (a JSON array of 64 bytes) or a base58-encoded 64-byte secret key. The secret is
/// never persisted, logged, or returned to the browser.
/// </summary>
public static class SolanaFaucetSecret
{
    public const string EnvironmentVariable = "ARTISANALBREW_SOLANA_ADMIN_KEY";

    /// <summary>
    /// Parses <paramref name="raw"/> and confirms it belongs to <paramref name="expectedAdministrator"/>.
    /// On success, <paramref name="secretKey64"/> is the canonical seed || public-key 64-byte key.
    /// </summary>
    public static bool TryLoad(string? raw, string expectedAdministrator, out byte[] secretKey64, out string? error)
    {
        secretKey64 = Array.Empty<byte>();
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = $"The Solana faucet mint-authority key ({EnvironmentVariable}) is not configured.";
            return false;
        }

        if (!TryParseBytes(raw.Trim(), out var bytes) || bytes.Length != 64)
        {
            error = $"{EnvironmentVariable} must be a 64-byte Solana secret key (JSON byte array or base58).";
            return false;
        }

        byte[] derivedPublicKey;
        try
        {
            var privateKey = new Ed25519PrivateKeyParameters(bytes, 0); // first 32 bytes are the seed
            derivedPublicKey = privateKey.GeneratePublicKey().GetEncoded();
        }
        catch (Exception exception)
        {
            error = $"{EnvironmentVariable} is not a valid Ed25519 secret key: {exception.Message}";
            return false;
        }

        if (!string.Equals(SolanaTransactionBuilder.EncodeKey(derivedPublicKey), expectedAdministrator, StringComparison.Ordinal))
        {
            error = $"{EnvironmentVariable} does not match the manifest administrator ({expectedAdministrator}).";
            return false;
        }

        // Rebuild canonically so the trailing public key is always correct regardless of the input.
        secretKey64 = new byte[64];
        Array.Copy(bytes, 0, secretKey64, 0, 32);
        derivedPublicKey.CopyTo(secretKey64, 32);
        return true;
    }

    private static bool TryParseBytes(string raw, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (raw.StartsWith('['))
        {
            try
            {
                // Solana CLI keypair format: a JSON array of integers, e.g. [174,47,...]. (System.Text.Json
                // maps byte[] to base64, so parse as int[] and range-check into bytes.)
                var values = JsonSerializer.Deserialize<int[]>(raw);
                if (values is null || Array.Exists(values, value => value is < 0 or > 255)) return false;
                bytes = Array.ConvertAll(values, value => (byte)value);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        return SolanaBase58.TryDecode(raw, out bytes);
    }
}
