using System.Text;

namespace ThisCafeteria.Domain.Avatars;

/// <summary>
/// Derives a wallet's starter robot from its address, so every account has a
/// distinct avatar before anyone opens the editor.
/// </summary>
/// <remarks>
/// This is an identicon in robot form: the address is the only input, so the
/// same wallet renders the same robot on any device, on a cold cache, and on
/// a server that has never seen it. A saved avatar overrides the seed; a
/// profile that has never been edited keeps rendering from it forever, which
/// is why the hash has to stay stable across processes and releases.
///
/// <see cref="string.GetHashCode()"/> is deliberately not used — .NET
/// randomises it per process, so the same wallet would get a different robot
/// on every restart. FNV-1a is specified, trivial and fixed.
/// </remarks>
public static class AvatarSeed
{
    private const ulong FnvOffsetBasis = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    /// <summary>
    /// The starter look for a wallet, or the catalog defaults when there is
    /// no wallet linked yet.
    /// </summary>
    public static RobotAvatar FromWallet(string? walletAddress)
    {
        var normalized = NormalizeAddress(walletAddress);
        if (normalized.Length == 0)
        {
            return AvatarCatalog.CreateDefault();
        }

        var avatar = new RobotAvatar();
        foreach (var slot in AvatarCatalog.Slots)
        {
            // Mixing the slot key into the input — rather than reusing one
            // hash and slicing bits out of it — keeps the slots independent,
            // so adding a seventh slot later does not reshuffle the six that
            // wallets are already showing.
            var index = (int)(Hash($"{normalized}:{slot.Key}") % (ulong)slot.Items.Count);
            avatar[slot.Key] = slot.Items[index].Id;
        }

        return avatar;
    }

    /// <summary>
    /// Whether <paramref name="avatar"/> is exactly what this wallet seeds to,
    /// i.e. the user has never actually chosen anything.
    /// </summary>
    public static bool IsUnchangedSeed(string? walletAddress, RobotAvatar? avatar) =>
        avatar is not null && FromWallet(walletAddress).HasSameLook(AvatarCatalog.Normalize(avatar));

    /// <summary>
    /// Case-folds EVM addresses only.
    /// </summary>
    /// <remarks>
    /// The same Ethereum wallet appears both checksummed and all-lowercase
    /// depending on which path wrote it, and those must seed the same robot.
    /// Base58 addresses (Solana) are case-significant and always presented in
    /// one form, so they are left alone — lower-casing them would collapse
    /// distinct wallets onto one avatar.
    /// </remarks>
    private static string NormalizeAddress(string? walletAddress)
    {
        var trimmed = walletAddress?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var isHex = trimmed.Length > 2 &&
                    trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                    IsHexDigits(trimmed.AsSpan(2));

        return isHex ? trimmed.ToLowerInvariant() : trimmed;
    }

    private static ulong Hash(string value)
    {
        var hash = FnvOffsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        return hash;
    }

    private static bool IsHexDigits(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
