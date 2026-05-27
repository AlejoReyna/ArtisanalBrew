using ThisCafeteria.Application.Services.Rewards;

namespace ThisCafeteria.Application.Services.Blockchain;

public sealed class CoffeeDashboardModel
{
    public string WalletAddress { get; init; } = string.Empty;
    public decimal NativeBalance { get; init; }
    public decimal PaymentTokenBalance { get; init; }
    public decimal StakedPaymentTokenBalance { get; init; }
    public decimal PendingStakingRewards { get; init; }
    public decimal CoffeeCoinBalance { get; init; }
    public decimal CurrentApr { get; init; }
    public decimal EstimatedDailyReward =>
        RewardCalculator.DailyRewardFromPaymentToken(StakedPaymentTokenBalance, CurrentApr);
}
