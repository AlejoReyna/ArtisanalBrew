using System.Numerics;
using FluentAssertions;
using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.UnitTests;

public sealed class ConfirmationCalculatorTests
{
    [Fact]
    public void Calculate_ShouldReturnOne_WhenReceiptIsInTheLatestBlock()
    {
        ConfirmationCalculator.Calculate(currentBlock: 100, receiptBlock: 100).Should().Be(1);
    }

    [Fact]
    public void Calculate_ShouldCountBlocksSinceReceipt()
    {
        ConfirmationCalculator.Calculate(currentBlock: 105, receiptBlock: 100).Should().Be(6);
    }

    [Fact]
    public void Calculate_ShouldReturnZero_WhenReceiptBlockIsNull()
    {
        ConfirmationCalculator.Calculate(currentBlock: 100, receiptBlock: null).Should().Be(0);
    }

    [Fact]
    public void Calculate_ShouldClampToZero_WhenReceiptBlockIsSomehowAheadOfCurrent()
    {
        ConfirmationCalculator.Calculate(currentBlock: 100, receiptBlock: 105).Should().Be(0);
    }

    [Fact]
    public void Calculate_ShouldHandleLargeBlockNumbers()
    {
        var current = BigInteger.Parse("99999999999999999999");
        var receipt = current - 3;

        ConfirmationCalculator.Calculate(current, receipt).Should().Be(4);
    }
}
