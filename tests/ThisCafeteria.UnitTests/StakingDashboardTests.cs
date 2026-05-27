using FluentAssertions;
using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.UnitTests;

public sealed class StakingDashboardTests
{
    [Fact]
    public void EstimatedDailyReward_ShouldUseStakedBalance_NotWalletBalance()
    {
        var dashboard = new CoffeeDashboardModel
        {
            PaymentTokenBalance = 100m,
            StakedPaymentTokenBalance = 10m,
            CurrentApr = 36.5m
        };

        dashboard.EstimatedDailyReward.Should().Be(0.01m);
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(0.5, 1, true)]
    [InlineData(1.1, 1, false)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    public void IsValidUnstakeAmount_ShouldRejectInvalidUnstakeAttempts(
        decimal amount,
        decimal stakedBalance,
        bool expected)
    {
        var result = StakingAmountRules.IsValidUnstakeAmount(amount, stakedBalance);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(0.0001, true)]
    public void IsValidStakeAmount_ShouldRequirePositiveAmount(decimal amount, bool expected)
    {
        var result = StakingAmountRules.IsValidStakeAmount(amount);

        result.Should().Be(expected);
    }
}
