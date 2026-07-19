using FluentAssertions;
using ThisCafeteria.Web.Controllers;

namespace ThisCafeteria.UnitTests;

public sealed class LiquidStakingControllerTests
{
    [Theory]
    [InlineData("1.23456789", 9, "1234567890")]
    [InlineData("1.234567890123456789", 18, "1234567890123456789")]
    public void ConvertsLedgerAmountsUsingTheDeploymentTokenPrecision(string value, int decimals, string expected)
    {
        LiquidStakingController.ToRaw(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), decimals)
            .Should().Be(expected);
    }
}
