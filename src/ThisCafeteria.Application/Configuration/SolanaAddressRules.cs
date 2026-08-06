using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.Application.Configuration;

/// <summary>
/// Solana program constants, plus the address predicates that read naturally at call sites.
///
/// The base58 algorithm itself lives in <see cref="SolanaBase58"/> and is delegated to here.
/// These two files each carried their own byte-identical copy of it until they were consolidated;
/// the decoding logic now exists exactly once in the solution.
/// </summary>
public static class SolanaAddressRules
{
    public const string TokenProgram = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
    public const string Token2022Program = "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb";

    public static bool IsPublicKey(string value) => SolanaBase58.IsPublicKey(value);

    public static bool TryDecode(string value, out byte[] bytes) =>
        SolanaBase58.TryDecode(value, out bytes);
}
