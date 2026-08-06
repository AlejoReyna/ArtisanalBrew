using System.Globalization;
using FluentAssertions;
using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.UnitTests;

public sealed class StakingAmountRulesTests
{
    [Theory]
    [InlineData("1.23456789", 9, "1234567890")]
    [InlineData("1.234567890123456789", 18, "1234567890123456789")]
    public void ConvertsLedgerAmountsUsingTheDeploymentTokenPrecision(string value, int decimals, string expected)
    {
        StakingAmountRules.ToRawAmount(decimal.Parse(value, CultureInfo.InvariantCulture), decimals)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("1.9999999999", 9, "1999999999")]
    [InlineData("0.0000000001", 9, "0")]
    public void TruncatesRatherThanRoundsSoTheLedgerNeverOverstatesWhatMoved(string value, int decimals, string expected)
    {
        StakingAmountRules.ToRawAmount(decimal.Parse(value, CultureInfo.InvariantCulture), decimals)
            .Should().Be(expected);
    }

    [Fact]
    public void DefaultsToTheEighteenDecimalsUsedByTheEvmStakingLedger()
    {
        StakingAmountRules.ToRawAmount(1.5m).Should().Be("1500000000000000000");
    }
}
