using System.Buffers.Binary;
using System.Numerics;
using FluentAssertions;
using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.UnitTests;

public sealed class SolanaAccountCodecTests
{
    [Fact]
    public void DecodesTheAnchorVaultLayoutAtTheCanonicalOffsets()
    {
        var bytes = new byte[SolanaAccountCodec.VaultAccountSize];
        bytes[9] = 9;
        bytes[10] = 6;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(141, 8), 123UL);
        new BigInteger(456UL).TryWriteBytes(bytes.AsSpan(149, 16), out _, isUnsigned: true, isBigEndian: false).Should().BeTrue();
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(165, 8), 789UL);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(173, 8), 1_000UL);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(181, 8), 900UL);

        var state = SolanaAccountCodec.DecodeVault(bytes);

        state.CafeDecimals.Should().Be(9);
        state.CoffeeDecimals.Should().Be(6);
        state.TotalShares.Should().Be(123UL);
        state.RewardPerShare.Should().Be(new BigInteger(456UL));
        state.RewardRate.Should().Be(789UL);
        state.PeriodFinish.Should().Be(1_000UL);
        state.LastRewardSlot.Should().Be(900UL);
    }

    [Fact]
    public void DecodesMintDecimalsAndAuthoritiesFromTheSplMintBaseLayout()
    {
        var bytes = new byte[82];
        bytes[44] = 9;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 1);
        Enumerable.Repeat((byte)7, 32).ToArray().CopyTo(bytes, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(46, 4), 1);
        Enumerable.Repeat((byte)8, 32).ToArray().CopyTo(bytes, 50);

        var state = SolanaAccountCodec.DecodeMint(bytes);
        state.Decimals.Should().Be(9);
        state.MintAuthority.Should().Equal(Enumerable.Repeat((byte)7, 32));
        state.FreezeAuthority.Should().Equal(Enumerable.Repeat((byte)8, 32));
    }
}
