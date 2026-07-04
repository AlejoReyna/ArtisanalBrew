using System.Numerics;
using FluentAssertions;
using Nethereum.Util;
using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.UnitTests;

public sealed class StakingCalldataDecoderTests
{
    private static string Selector(string signature) => StakingCalldataDecoder.FunctionSelector(signature);

    private static string EncodeUint256(BigInteger value) => value.ToString("x").PadLeft(64, '0');

    [Fact]
    public void TryDecodeStakingAmount_ShouldDecodeStakeCalldata()
    {
        var input = $"{Selector("stake(uint256)")}{EncodeUint256(1500)}";

        var result = StakingCalldataDecoder.TryDecodeStakingAmount(input, StakingTransactionType.Stake, out var amount);

        result.Should().BeTrue();
        amount.Should().Be(1500);
    }

    [Theory]
    [InlineData("unstake(uint256)")]
    [InlineData("withdraw(uint256)")]
    public void TryDecodeStakingAmount_ShouldDecodeUnstakeCalldata_ForEitherSelector(string signature)
    {
        var input = $"{Selector(signature)}{EncodeUint256(750)}";

        var result = StakingCalldataDecoder.TryDecodeStakingAmount(input, StakingTransactionType.Unstake, out var amount);

        result.Should().BeTrue();
        amount.Should().Be(750);
    }

    [Fact]
    public void TryDecodeStakingAmount_ShouldRejectWrongSelectorForTransactionType()
    {
        var input = $"{Selector("stake(uint256)")}{EncodeUint256(100)}";

        var result = StakingCalldataDecoder.TryDecodeStakingAmount(input, StakingTransactionType.Unstake, out var amount);

        result.Should().BeFalse();
        amount.Should().Be(BigInteger.Zero);
    }

    [Fact]
    public void TryDecodeStakingAmount_ShouldRejectClaimSelector()
    {
        var input = $"{Selector("getReward()")}";

        var result = StakingCalldataDecoder.TryDecodeStakingAmount(input, StakingTransactionType.Stake, out _);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0x")]
    [InlineData("0x1234")]
    [InlineData("not-hex-data-at-all")]
    public void TryDecodeStakingAmount_ShouldRejectMalformedInput(string? input)
    {
        var result = StakingCalldataDecoder.TryDecodeStakingAmount(input, StakingTransactionType.Stake, out var amount);

        result.Should().BeFalse();
        amount.Should().Be(BigInteger.Zero);
    }

    [Theory]
    [InlineData("getReward()")]
    [InlineData("claimReward()")]
    public void TryDecodeClaimSelector_ShouldAcceptEitherCandidateSelector(string signature)
    {
        var input = Selector(signature);

        StakingCalldataDecoder.TryDecodeClaimSelector(input).Should().BeTrue();
    }

    [Fact]
    public void TryDecodeClaimSelector_ShouldRejectStakeSelector()
    {
        var input = Selector("stake(uint256)");

        StakingCalldataDecoder.TryDecodeClaimSelector(input).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0x")]
    public void TryDecodeClaimSelector_ShouldRejectMalformedInput(string? input)
    {
        StakingCalldataDecoder.TryDecodeClaimSelector(input).Should().BeFalse();
    }

    [Fact]
    public void TryDecodeErc20Transfer_ShouldDecodeRecipientAndAmount()
    {
        const string recipient = "0x9D5305A9621AAFb5b5F8ba7a9977e3d96ea7eceB";
        var input = $"0xa9059cbb{recipient[2..].PadLeft(64, '0')}{EncodeUint256(2500)}";

        var result = StakingCalldataDecoder.TryDecodeErc20Transfer(input, out var decodedRecipient, out var amount);

        result.Should().BeTrue();
        amount.Should().Be(2500);
        AddressUtil.Current.AreAddressesTheSame(decodedRecipient, recipient).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0x1234")]
    [InlineData("0xdeadbeef")]
    public void TryDecodeErc20Transfer_ShouldRejectMalformedInput(string? input)
    {
        var result = StakingCalldataDecoder.TryDecodeErc20Transfer(input, out var recipient, out var amount);

        result.Should().BeFalse();
        recipient.Should().BeEmpty();
        amount.Should().Be(BigInteger.Zero);
    }
}
