using Microsoft.AspNetCore.Mvc;
using Nethereum.Util;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Application.Services.Rewards;

namespace ThisCafeteria.Web.Controllers;

[Route("rewards")]
public sealed class RewardsController(
    IRewardClaimService rewardClaimService,
    ILoyaltyMintService loyaltyMintService,
    BlockchainNetworkOptions chain) : Controller
{
    [HttpGet("api/claimable")]
    public async Task<IActionResult> GetClaimableAsync(
        [FromQuery] string walletAddress,
        CancellationToken cancellationToken)
    {
        if (!WalletAddressRules.TryNormalizeWallet(walletAddress, out var wallet))
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
        if (!WalletAddressRules.TryNormalizeWallet(request.WalletAddress, out var wallet))
        {
            return BadRequest("A valid wallet address is required.");
        }

        var result = await rewardClaimService.ClaimDailyRewardAsync(wallet, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("api/mint-loyalty")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MintLoyaltyAsync(
        [FromBody] MintLoyaltyRequest request,
        CancellationToken cancellationToken)
    {
        if (!WalletAddressRules.TryNormalizeWallet(request.WalletAddress, out var wallet))
        {
            return BadRequest("A valid wallet address is required.");
        }

        if (!TryResolveCurrentWallet(out var sessionWallet))
        {
            return Unauthorized("Connect or sign in with your wallet before minting allocation rewards.");
        }

        if (!AddressUtil.Current.AreAddressesTheSame(wallet, sessionWallet))
        {
            return BadRequest("The connected wallet does not match the allocation session wallet.");
        }

        if (request.Amount <= 0m)
        {
            return BadRequest("Loyalty reward amount must be greater than zero.");
        }

        if (request.PaymentAmount <= 0m)
        {
            return BadRequest("Payment amount must be greater than zero.");
        }

        if (!WalletAddressRules.TryNormalizeTransactionHash(request.PaymentTransactionHash, out var paymentTransactionHash))
        {
            return BadRequest("A valid payment token transaction hash is required.");
        }

        var result = await loyaltyMintService.MintAsync(
            chain,
            new LoyaltyMintCommand(
                wallet,
                request.Amount,
                request.PaymentAmount,
                paymentTransactionHash,
                request.AllocationName),
            cancellationToken);

        return result.Status switch
        {
            LoyaltyMintStatus.Minted => Ok(new RewardClaimResultModel
            {
                Success = true,
                TransactionHash = result.MintTransactionHash,
                PaymentTransactionHash = paymentTransactionHash,
                MintedAmount = request.Amount
            }),
            LoyaltyMintStatus.AlreadyClaimed => Conflict("Esta transacci\u00f3n ya ha sido reclamada."),
            LoyaltyMintStatus.PendingConfirmations => StatusCode(StatusCodes.Status202Accepted, new
            {
                success = false,
                status = "pending_confirmations"
            }),
            LoyaltyMintStatus.MintingNotConfigured => BadRequest("Minting is not configured on the server."),
            LoyaltyMintStatus.MintFailed => BadRequest(result.Error),
            _ => BadRequest(
                "Payment transaction could not be verified on-chain. It must be a successful configured ERC-20 payment token transfer from your wallet to the configured marketplace wallet for the exact coffee price.")
        };
    }

    private bool TryResolveCurrentWallet(out string wallet)
    {
        wallet = string.Empty;

        var candidates = new[]
        {
            User.FindFirst("wallet_address")?.Value,
            User.Identity?.Name,
            HttpContext.Session.GetString("WalletAddress")
        };

        foreach (var candidate in candidates)
        {
            if (WalletAddressRules.TryNormalizeWallet(candidate, out wallet))
            {
                return true;
            }
        }

        return false;
    }

    public sealed record WalletRequest(string WalletAddress);

    public sealed record MintLoyaltyRequest(
        string WalletAddress,
        decimal Amount,
        decimal PaymentAmount,
        string PaymentTransactionHash,
        string? AllocationName);
}
