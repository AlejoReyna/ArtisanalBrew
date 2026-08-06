using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services.Rewards;

public sealed class LoyaltyMintService(
    ICoffeeWeb3Service web3Service,
    IRewardClaimRepository claims,
    IUnitOfWork unitOfWork) : ILoyaltyMintService
{
    private const int AllocationNameMaxLength = 120;

    public async Task<LoyaltyMintResult> MintAsync(
        BlockchainNetworkOptions chain,
        LoyaltyMintCommand command,
        CancellationToken cancellationToken = default)
    {
        // Cheap pre-check outside the transaction. The authoritative check is the one inside it.
        if (await claims.ExistsByPaymentHashAsync(command.PaymentTransactionHash, cancellationToken).ConfigureAwait(false))
        {
            return LoyaltyMintResult.AlreadyClaimed;
        }

        var verificationStatus = await web3Service
            .VerifyPaymentTransactionAsync(
                command.PaymentTransactionHash,
                command.WalletAddress,
                command.PaymentAmount,
                cancellationToken)
            .ConfigureAwait(false);

        if (verificationStatus == TransactionVerificationStatus.PendingConfirmations)
        {
            return LoyaltyMintResult.PendingConfirmations;
        }

        if (verificationStatus != TransactionVerificationStatus.Verified)
        {
            return LoyaltyMintResult.PaymentNotVerified;
        }

        if (!web3Service.IsMintingConfigured)
        {
            return LoyaltyMintResult.MintingNotConfigured;
        }

        await using var transaction = await unitOfWork
            .BeginSerializableTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Re-checked under serializable isolation: two requests can both clear the pre-check above.
        if (await claims.ExistsByPaymentHashAsync(command.PaymentTransactionHash, cancellationToken).ConfigureAwait(false))
        {
            return LoyaltyMintResult.AlreadyClaimed;
        }

        var claim = BuildClaim(chain, command);

        if (!await claims.TryAddAsync(claim, cancellationToken).ConfigureAwait(false))
        {
            return LoyaltyMintResult.AlreadyClaimed;
        }

        string mintTransactionHash;

        try
        {
            mintTransactionHash = await web3Service
                .MintCoffeeCoinAsync(command.WalletAddress, command.Amount, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Returning without committing rolls the claim back, so a failed mint leaves no
            // record behind and the payment stays claimable once the cause is fixed.
            return LoyaltyMintResult.MintFailed(exception.Message);
        }

        claim.TransactionHash = mintTransactionHash;
        claim.MintExplorerUrl = BuildExplorerTransactionUrl(chain, mintTransactionHash);

        await claims.UpdateAsync(claim, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return LoyaltyMintResult.Minted(mintTransactionHash);
    }

    private static RewardClaim BuildClaim(BlockchainNetworkOptions chain, LoyaltyMintCommand command) => new()
    {
        WalletAddress = command.WalletAddress,
        Amount = command.Amount,
        ClaimType = "allocation",
        PaymentTransactionHash = command.PaymentTransactionHash,
        PaymentAmount = command.PaymentAmount,
        PaymentChainId = chain.ChainId,
        PaymentNetworkName = chain.NetworkName,
        PaymentTokenContract = chain.EffectivePaymentTokenContract,
        MarketplaceWallet = chain.MarketplaceWallet,
        AllocationName = NormalizeAllocationName(command.AllocationName),
        PaymentExplorerUrl = BuildExplorerTransactionUrl(chain, command.PaymentTransactionHash),
        ClaimedAtUtc = DateTime.UtcNow
    };

    private static string? NormalizeAllocationName(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized[..Math.Min(normalized.Length, AllocationNameMaxLength)];
    }

    private static string BuildExplorerTransactionUrl(BlockchainNetworkOptions chain, string transactionHash)
    {
        var explorer = chain.ExplorerUrl?.Trim();
        return string.IsNullOrWhiteSpace(explorer)
            ? string.Empty
            : $"{explorer.TrimEnd('/')}/tx/{transactionHash}";
    }
}
