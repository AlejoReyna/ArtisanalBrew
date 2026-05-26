namespace ThisCafeteria.Application.Services.Rewards;

public static class RewardCalculator
{
    /// <summary>
    /// Estimates daily COFFEE rewards from the configured payment token balance and APR.
    /// </summary>
    public static decimal DailyRewardFromPaymentToken(decimal paymentTokenBalance, decimal aprPercent) =>
        paymentTokenBalance <= 0m || aprPercent <= 0m
            ? 0m
            : paymentTokenBalance * (aprPercent / 365m / 100m);
}
