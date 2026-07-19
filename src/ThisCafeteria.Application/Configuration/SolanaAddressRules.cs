namespace ThisCafeteria.Application.Configuration;

public static class SolanaAddressRules
{
    public const string TokenProgram = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
    public const string Token2022Program = "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb";
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    public static bool IsPublicKey(string value) => TryDecode(value, out var bytes) && bytes.Length == 32;

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
            while (carry > 0)
            {
                digits[length++] = (byte)(carry % 256);
                carry /= 256;
            }
        }
        while (length > 0 && digits[length - 1] == 0) length--;
        var leadingZeroes = value.TakeWhile(character => character == '1').Count();
        bytes = new byte[leadingZeroes + length];
        for (var index = 0; index < length; index++) bytes[bytes.Length - 1 - index] = digits[index];
        return true;
    }
}
