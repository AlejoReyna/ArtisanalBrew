using System.Text;

namespace ThisCafeteria.Application.Services.Blockchain;

/// <summary>
/// Base58 encoding for Solana public keys and signatures.
///
/// This lives in Application, alongside <see cref="SolanaAccountCodec"/> and
/// <c>WalletAddressRules</c>, because it is a pure value conversion with no I/O - the same
/// category of helper, and needed by the Web host, the Worker, and the Infrastructure gateways
/// alike. It previously existed as two byte-identical <c>internal</c> copies, one in the Solana
/// staking gateway and one in the Worker's reconciliation supervisor.
///
/// The body is preserved verbatim from those copies. A base58 codec is exactly the kind of code
/// where a well-intentioned reformat can silently change an address, so it was moved unedited.
/// </summary>
public static class SolanaBase58
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    public static bool IsPublicKey(string value) => TryDecode(value, out var bytes) && bytes.Length == 32;
    public static bool TryDecode(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>(); if (string.IsNullOrWhiteSpace(value)) return false;
        var digits = new byte[(value.Length * 733 / 1000) + 1]; var length = 1;
        foreach (var character in value) { var carry = Alphabet.IndexOf(character); if (carry < 0) return false; for (var i = 0; i < length; i++) { carry += 58 * digits[i]; digits[i] = (byte)(carry % 256); carry /= 256; } while (carry > 0) { digits[length++] = (byte)(carry % 256); carry /= 256; } }
        while (length > 0 && digits[length - 1] == 0) length--; var zeros = value.TakeWhile(c => c == '1').Count(); bytes = new byte[zeros + length]; for (var i = 0; i < length; i++) bytes[bytes.Length - 1 - i] = digits[i]; return true;
    }
    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        var digits = new byte[bytes.Length * 138 / 100 + 1]; var length = 1;
        foreach (var value in bytes) { var carry = (int)value; for (var i = 0; i < length; i++) { carry += 256 * digits[i]; digits[i] = (byte)(carry % 58); carry /= 58; } while (carry > 0) { digits[length++] = (byte)(carry % 58); carry /= 58; } }
        var result = new StringBuilder(bytes.Length * 2); for (var i = 0; i < bytes.Length && bytes[i] == 0; i++) result.Append('1'); for (var i = length - 1; i >= 0; i--) result.Append(Alphabet[digits[i]]); return result.ToString();
    }
}
