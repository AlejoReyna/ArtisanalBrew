using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Nethereum.Util;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Application.Services.Rewards;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.Web.Controllers;

[Route("rewards")]
[IgnoreAntiforgeryToken]
public sealed class RewardsController(
    IRewardClaimService rewardClaimService,
    ICoffeeWeb3Service web3Service,
    AppDbContext dbContext) : Controller
{
    [HttpGet("api/claimable")]
    public async Task<IActionResult> GetClaimableAsync(
        [FromQuery] string walletAddress,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeWallet(walletAddress, out var wallet))
        {
            return BadRequest("A valid wallet address is required.");
        }

        var status = await rewardClaimService.GetClaimStatusAsync(wallet, cancellationToken);
        return Ok(status);
    }

    [HttpPost("api/claim")]
    public async Task<IActionResult> ClaimDailyAsync(
        [FromBody] WalletRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeWallet(request.WalletAddress, out var wallet))
        {
            return BadRequest("A valid wallet address is required.");
        }

        var result = await rewardClaimService.ClaimDailyRewardAsync(wallet, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("api/mint-loyalty")]
    public async Task<IActionResult> MintLoyaltyAsync(
        [FromBody] MintLoyaltyRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeWallet(request.WalletAddress, out var wallet))
        {
            return BadRequest("A valid wallet address is required.");
        }

        if (request.Amount <= 0m)
        {
            return BadRequest("Loyalty reward amount must be greater than zero.");
        }

        if (request.PaymentAmount <= 0m)
        {
            return BadRequest("Payment amount must be greater than zero.");
        }

        if (!TryNormalizeTransactionHash(request.PaymentTransactionHash, out var paymentTransactionHash))
        {
            return BadRequest("A valid ankrBNB payment transaction hash is required.");
        }

        if (await PaymentHashExistsAsync(paymentTransactionHash, cancellationToken))
        {
            return Conflict("Esta transacción ya ha sido reclamada.");
        }

        var verified = await web3Service.VerifyPaymentTransactionAsync(
            paymentTransactionHash,
            wallet,
            request.PaymentAmount,
            cancellationToken);

        if (!verified)
        {
            return BadRequest(
                "Payment transaction could not be verified on-chain. It must be a successful ankrBNB transfer from your wallet to the configured marketplace wallet for the exact coffee price.");
        }

        if (!web3Service.IsMintingConfigured)
        {
            return BadRequest("Minting is not configured on the server.");
        }

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        if (await PaymentHashExistsAsync(paymentTransactionHash, cancellationToken))
        {
            return Conflict("Esta transacción ya ha sido reclamada.");
        }

        var claim = new RewardClaim
        {
            WalletAddress = wallet,
            Amount = request.Amount,
            ClaimType = "loyalty",
            PaymentTransactionHash = paymentTransactionHash,
            ClaimedAtUtc = DateTime.UtcNow
        };

        dbContext.RewardClaims.Add(claim);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict("Esta transacción ya ha sido reclamada.");
        }

        string mintTransactionHash;
        try
        {
            mintTransactionHash = await web3Service.MintCoffeeCoinAsync(
                wallet,
                request.Amount,
                cancellationToken);
        }
        catch (Exception exception)
        {
            return BadRequest(exception.Message);
        }

        claim.TransactionHash = mintTransactionHash;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var result = new RewardClaimResultModel
        {
            Success = true,
            TransactionHash = mintTransactionHash,
            PaymentTransactionHash = paymentTransactionHash,
            MintedAmount = request.Amount
        };

        return Ok(result);
    }

    private static bool TryNormalizeWallet(string? address, out string checksum)
    {
        checksum = string.Empty;
        if (string.IsNullOrWhiteSpace(address) ||
            !AddressUtil.Current.IsValidEthereumAddressHexFormat(address))
        {
            return false;
        }

        checksum = AddressUtil.Current.ConvertToChecksumAddress(address);
        return true;
    }

    private Task<bool> PaymentHashExistsAsync(
        string paymentTransactionHash,
        CancellationToken cancellationToken) =>
        dbContext.RewardClaims.AnyAsync(
            claim => claim.PaymentTransactionHash == paymentTransactionHash,
            cancellationToken);

    private static bool TryNormalizeTransactionHash(string? value, out string transactionHash)
    {
        transactionHash = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != 66 ||
            !value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            !value[2..].All(Uri.IsHexDigit))
        {
            return false;
        }

        transactionHash = value.ToLowerInvariant();
        return true;
    }

    public sealed record WalletRequest(string WalletAddress);

    public sealed record MintLoyaltyRequest(
        string WalletAddress,
        decimal Amount,
        decimal PaymentAmount,
        string PaymentTransactionHash);
}
