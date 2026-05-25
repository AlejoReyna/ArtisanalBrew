namespace ThisCafeteria.Application.Services.Rewards;

public static class RewardCalculator
{
    /// <summary>
    /// Estimates daily COFFEE rewards from liquid-staked ankrBNB balance and APR (same units as ankrBNB).
    /// </summary>
    public static decimal DailyRewardFromAnkrBnb(decimal ankrBnbBalance, decimal aprPercent) =>
        ankrBnbBalance <= 0m || aprPercent <= 0m
            ? 0m
            : ankrBnbBalance * (aprPercent / 365m / 100m);
}
