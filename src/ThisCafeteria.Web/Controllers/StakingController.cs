using Microsoft.AspNetCore.Mvc;
using Nethereum.Util;
using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.Web.Controllers;

[Route("staking")]
[IgnoreAntiforgeryToken]
public sealed class StakingController(ICoffeeWeb3Service web3Service) : Controller
{
    private const string WalletSessionKey = "WalletAddress";

    [HttpGet("api/coffee-balance")]
    public async Task<IActionResult> GetCoffeeBalanceAsync(
        [FromQuery] string walletAddress,
        CancellationToken cancellationToken)
    {
        if (!AddressUtil.Current.IsValidEthereumAddressHexFormat(walletAddress))
        {
            return BadRequest("A valid wallet address is required.");
        }

        var checksum = AddressUtil.Current.ConvertToChecksumAddress(walletAddress);
        var balance = await web3Service.GetCoffeeCoinBalanceAsync(checksum, cancellationToken);
        return Ok(new { walletAddress = checksum, balance, symbol = "COFFEE" });
    }

    [HttpGet("api/dashboard")]
    public async Task<IActionResult> GetDashboardAsync([FromQuery] string? walletAddress, CancellationToken cancellationToken)
    {
        var wallet = ResolveWalletAddress(walletAddress);
        if (string.IsNullOrWhiteSpace(wallet))
        {
            return Ok(new CoffeeDashboardModel());
        }

        if (!AddressUtil.Current.IsValidEthereumAddressHexFormat(wallet))
        {
            return BadRequest("A valid wallet address is required.");
        }

        var checksum = AddressUtil.Current.ConvertToChecksumAddress(wallet);
        var model = await web3Service.GetDashboardDataAsync(checksum, cancellationToken);
        return Ok(model);
    }

    [HttpPost("save-wallet-session")]
    public IActionResult SaveWalletSession([FromBody] SaveWalletSessionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WalletAddress) ||
            !AddressUtil.Current.IsValidEthereumAddressHexFormat(request.WalletAddress))
        {
            return BadRequest("A valid wallet address is required.");
        }

        var checksum = AddressUtil.Current.ConvertToChecksumAddress(request.WalletAddress);
        HttpContext.Session.SetString(WalletSessionKey, checksum);
        return Ok();
    }

    [HttpPost("clear-wallet-session")]
    public IActionResult ClearWalletSession()
    {
        HttpContext.Session.Remove(WalletSessionKey);
        return Redirect("/staking");
    }

    private string? ResolveWalletAddress(string? queryWallet)
    {
        if (!string.IsNullOrWhiteSpace(queryWallet))
        {
            return queryWallet;
        }

        return HttpContext.Session.GetString(WalletSessionKey);
    }

    public sealed record SaveWalletSessionRequest(string WalletAddress);
}
