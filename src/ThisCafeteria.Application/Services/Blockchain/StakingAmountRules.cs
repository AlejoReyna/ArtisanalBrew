using System.Globalization;
using System.Numerics;

namespace ThisCafeteria.Application.Services.Blockchain;

public static class StakingAmountRules
{
    /// <summary>Wei, or the equivalent smallest unit for an 18-decimal token.</summary>
    private const int DefaultTokenDecimals = 18;

    public static bool IsValidStakeAmount(decimal amount) => amount > 0m;

    public static bool IsValidUnstakeAmount(decimal amount, decimal stakedBalance) =>
        amount > 0m &&
        stakedBalance > 0m &&
        amount <= stakedBalance;

    /// <summary>
    /// Scales a human-readable amount to the token's smallest unit, as the decimal string the
    /// ledger stores. Truncates rather than rounds: the on-chain value is the truth, and rounding
    /// up would claim more was moved than actually was.
    /// </summary>
    public static string ToRawAmount(decimal value, int decimals = DefaultTokenDecimals) =>
        decimal.Truncate(value * (decimal)BigInteger.Pow(10, decimals))
            .ToString("0", CultureInfo.InvariantCulture);
}
