using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.Application.Services.Rewards;

public interface IRewardClaimService
{
    Task<RewardClaimStatusModel> GetClaimStatusAsync(
        string walletAddress,
        CancellationToken cancellationToken = default);

    Task<RewardClaimResultModel> ClaimDailyRewardAsync(
        string walletAddress,
        CancellationToken cancellationToken = default);

    Task<RewardClaimResultModel> MintLoyaltyRewardAsync(
        string walletAddress,
        decimal amount,
        string paymentTransactionHash,
        CancellationToken cancellationToken = default);
}
