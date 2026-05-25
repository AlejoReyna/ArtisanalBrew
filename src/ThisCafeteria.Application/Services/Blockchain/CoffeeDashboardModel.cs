using ThisCafeteria.Application.Services.Rewards;

namespace ThisCafeteria.Application.Services.Blockchain;

public sealed class CoffeeDashboardModel
{
    public string WalletAddress { get; init; } = string.Empty;
    public decimal BnbBalance { get; init; }
    public decimal AnkrBnbBalance { get; init; }
    public decimal CoffeeCoinBalance { get; init; }
    public decimal CurrentApr { get; init; }
    public decimal EstimatedDailyReward =>
        RewardCalculator.DailyRewardFromAnkrBnb(AnkrBnbBalance, CurrentApr);
}
