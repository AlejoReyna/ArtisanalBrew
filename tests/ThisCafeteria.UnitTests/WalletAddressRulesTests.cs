using FluentAssertions;
using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.UnitTests;

public sealed class WalletAddressRulesTests
{
    private const string LowercaseAddress = "0x9d5305a9621aafb5b5f8ba7a9977e3d96ea7eceb";
    private const string ChecksumAddress = "0x9D5305A9621AAFb5b5F8ba7a9977e3d96ea7eceB";

    [Fact]
    public void TryNormalizeWallet_ShouldChecksumAValidLowercaseAddress()
    {
        var result = WalletAddressRules.TryNormalizeWallet(LowercaseAddress, out var checksum);

        result.Should().BeTrue();
        checksum.Should().Be(ChecksumAddress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("0x123")]
    [InlineData("0xZZZZ305A9621AAFb5b5F8ba7a9977e3d96ea7eceB")]
    public void TryNormalizeWallet_ShouldRejectInvalidAddresses(string? address)
    {
        var result = WalletAddressRules.TryNormalizeWallet(address, out var checksum);

        result.Should().BeFalse();
        checksum.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ChecksumAddress, true)]
    [InlineData("0x0000000000000000000000000000000000000000", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("0xshort", false)]
    public void IsConfiguredAddress_ShouldRejectEmptyAndZeroAddress(string? address, bool expected)
    {
        WalletAddressRules.IsConfiguredAddress(address).Should().Be(expected);
    }

    [Fact]
    public void TryNormalizeTransactionHash_ShouldLowercaseAValidHash()
    {
        const string hash = "0xABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF012345678A";

        var result = WalletAddressRules.TryNormalizeTransactionHash(hash, out var normalized);

        result.Should().BeTrue();
        normalized.Should().Be(hash.ToLowerInvariant());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0x1234")]
    [InlineData("not-a-hash")]
    public void TryNormalizeTransactionHash_ShouldRejectMalformedHashes(string? value)
    {
        var result = WalletAddressRules.TryNormalizeTransactionHash(value, out var transactionHash);

        result.Should().BeFalse();
        transactionHash.Should().BeEmpty();
    }

    [Fact]
    public void TryNormalizeTransactionHash_ShouldRejectHashOfWrongLength()
    {
        var tooShort = "0x" + new string('a', 63);

        WalletAddressRules.TryNormalizeTransactionHash(tooShort, out _).Should().BeFalse();
    }
}
