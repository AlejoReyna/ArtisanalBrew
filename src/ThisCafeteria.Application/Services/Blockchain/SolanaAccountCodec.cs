using System.Buffers.Binary;
using System.Numerics;

namespace ThisCafeteria.Application.Services.Blockchain;

public sealed record SolanaVaultAccountState(
    int CafeDecimals,
    int CoffeeDecimals,
    ulong TotalShares,
    BigInteger RewardPerShare,
    ulong RewardRate,
    ulong PeriodFinish,
    ulong LastRewardSlot);

public sealed record SolanaMintAccountState(int Decimals, byte[]? MintAuthority, byte[]? FreezeAuthority);

public static class SolanaAccountCodec
{
    public const int VaultAccountSize = 189;

    public static SolanaVaultAccountState DecodeVault(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < VaultAccountSize) throw new InvalidDataException("The Solana vault account is truncated.");
        return new SolanaVaultAccountState(
            bytes[9],
            bytes[10],
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[141..149]),
            new BigInteger(bytes[149..165], isUnsigned: true, isBigEndian: false),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[165..173]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[173..181]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[181..189]));
    }

    public static SolanaMintAccountState DecodeMint(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 82) throw new InvalidDataException("The Solana mint account is truncated.");
        return new SolanaMintAccountState(
            bytes[44],
            ReadOptionalPublicKey(bytes, 0, 4),
            ReadOptionalPublicKey(bytes, 46, 50));
    }

    public static int DecodeMintDecimals(ReadOnlySpan<byte> bytes) => DecodeMint(bytes).Decimals;

    private static byte[]? ReadOptionalPublicKey(ReadOnlySpan<byte> bytes, int optionOffset, int keyOffset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(optionOffset, 4)) == 1
            ? bytes.Slice(keyOffset, 32).ToArray()
            : null;
}
