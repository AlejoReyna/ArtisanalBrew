using Microsoft.AspNetCore.Mvc;
using Nethereum.Util;
using ThisCafeteria.Application.Services.Rewards;

namespace ThisCafeteria.Web.Controllers;

[Route("rewards")]
[IgnoreAntiforgeryToken]
public sealed class RewardsController(IRewardClaimService rewardClaimService) : Controller
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

        var result = await rewardClaimService.MintLoyaltyRewardAsync(
            wallet,
            request.Amount,
            request.PaymentTransactionHash,
            cancellationToken);

        return result.Success ? Ok(result) : BadRequest(result);
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

    public sealed record WalletRequest(string WalletAddress);

    public sealed record MintLoyaltyRequest(
        string WalletAddress,
        decimal Amount,
        string PaymentTransactionHash);
}
