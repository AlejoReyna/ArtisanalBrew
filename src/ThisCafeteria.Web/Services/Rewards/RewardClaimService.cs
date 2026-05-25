using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Application.Services.Rewards;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Web.Services.Rewards;

public sealed class RewardClaimService(
    ICoffeeWeb3Service web3Service,
    IRewardClaimRepository claimRepository) : IRewardClaimService
{
    public async Task<RewardClaimStatusModel> GetClaimStatusAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        var dashboard = await web3Service.GetDashboardDataAsync(walletAddress, cancellationToken)
            .ConfigureAwait(false);
        var lastClaim = await claimRepository
            .GetLatestDailyClaimAsync(walletAddress, cancellationToken)
            .ConfigureAwait(false);

        var dailyReward = RewardCalculator.DailyRewardFromAnkrBnb(
            dashboard.AnkrBnbBalance,
            dashboard.CurrentApr);

        var canClaimToday = CanClaimToday(lastClaim?.ClaimedAtUtc);
        var claimable = canClaimToday && dailyReward > 0m ? dailyReward : 0m;

        return new RewardClaimStatusModel
        {
            WalletAddress = walletAddress,
            AnkrBnbBalance = dashboard.AnkrBnbBalance,
            EstimatedDailyReward = dailyReward,
            ClaimableAmount = claimable,
            CanClaimToday = canClaimToday && claimable > 0m,
            MintingEnabled = web3Service.IsMintingConfigured,
            LastClaimedAtUtc = lastClaim?.ClaimedAtUtc
        };
    }

    public async Task<RewardClaimResultModel> ClaimDailyRewardAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        var status = await GetClaimStatusAsync(walletAddress, cancellationToken).ConfigureAwait(false);

        if (!status.MintingEnabled)
        {
            return new RewardClaimResultModel
            {
                Success = false,
                Error = "Minting is not configured. Add CoffeeCoinOwner:PrivateKey to User Secrets."
            };
        }

        if (!status.CanClaimToday || status.ClaimableAmount <= 0m)
        {
            return new RewardClaimResultModel
            {
                Success = false,
                Error = status.ClaimableAmount <= 0m
                    ? "No claimable rewards. Stake BNB to receive ankrBNB first."
                    : "You already claimed today's reward. Come back tomorrow."
            };
        }

        return await MintAndRecordAsync(
            walletAddress,
            status.ClaimableAmount,
            "daily",
            paymentTransactionHash: null,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<RewardClaimResultModel> MintLoyaltyRewardAsync(
        string walletAddress,
        decimal amount,
        string paymentTransactionHash,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0m)
        {
            return Task.FromResult(new RewardClaimResultModel
            {
                Success = false,
                Error = "Loyalty reward amount must be greater than zero."
            });
        }

        if (!IsTransactionHash(paymentTransactionHash))
        {
            return Task.FromResult(new RewardClaimResultModel
            {
                Success = false,
                Error = "A valid ankrBNB payment transaction hash is required."
            });
        }

        if (!web3Service.IsMintingConfigured)
        {
            return Task.FromResult(new RewardClaimResultModel
            {
                Success = false,
                Error = "Minting is not configured on the server."
            });
        }

        return MintAndRecordAsync(
            walletAddress,
            amount,
            "loyalty",
            paymentTransactionHash,
            cancellationToken);
    }

    private async Task<RewardClaimResultModel> MintAndRecordAsync(
        string walletAddress,
        decimal amount,
        string claimType,
        string? paymentTransactionHash,
        CancellationToken cancellationToken)
    {
        try
        {
            var txHash = await web3Service
                .MintCoffeeCoinAsync(walletAddress, amount, cancellationToken)
                .ConfigureAwait(false);

            await claimRepository.AddAsync(
                new RewardClaim
                {
                    WalletAddress = walletAddress,
                    Amount = amount,
                    ClaimType = claimType,
                    TransactionHash = txHash,
                    PaymentTransactionHash = paymentTransactionHash,
                    ClaimedAtUtc = DateTime.UtcNow
                },
                cancellationToken).ConfigureAwait(false);

            return new RewardClaimResultModel
            {
                Success = true,
                TransactionHash = txHash,
                PaymentTransactionHash = paymentTransactionHash,
                MintedAmount = amount
            };
        }
        catch (Exception exception)
        {
            return new RewardClaimResultModel
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    private static bool CanClaimToday(DateTime? lastClaimedAtUtc)
    {
        if (lastClaimedAtUtc is null)
        {
            return true;
        }

        return lastClaimedAtUtc.Value.Date < DateTime.UtcNow.Date;
    }

    private static bool IsTransactionHash(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length == 66 &&
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
        value[2..].All(Uri.IsHexDigit);
}
