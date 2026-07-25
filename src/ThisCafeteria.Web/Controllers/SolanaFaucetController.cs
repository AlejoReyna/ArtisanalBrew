using Microsoft.AspNetCore.Mvc;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Web.Services.Blockchain;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Route("faucet/api/solana")]
public sealed class SolanaFaucetController(
    ISolanaFaucetService faucet,
    IChainRegistry registry,
    ISelectedChainAccessor selectedChain) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string chainKey, [FromQuery] string walletIdentifier, CancellationToken cancellationToken)
    {
        if (!TryResolve(chainKey, walletIdentifier, out var chain, out var wallet)) return BadRequest("A valid enabled Solana chain and wallet are required.");
        if (!WalletMatchesSession(wallet)) return Unauthorized("Connect the wallet selected for this session.");
        return Ok(await faucet.GetStatusAsync(chain.Key, wallet, cancellationToken));
    }

    [HttpPost("claim")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Claim([FromBody] SolanaFaucetClaimRequest request, CancellationToken cancellationToken)
    {
        if (!TryResolve(request.ChainKey, request.WalletIdentifier, out var chain, out var wallet)) return BadRequest("A valid enabled Solana chain and wallet are required.");
        if (!string.Equals(selectedChain.SelectedChainKey, chain.Key, StringComparison.Ordinal)) return BadRequest("The faucet chain does not match the selected chain.");
        if (!WalletMatchesSession(wallet)) return Unauthorized("The authenticated wallet does not match this claim.");

        var result = await faucet.ClaimAsync(chain.Key, wallet, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    private bool TryResolve(string chainKey, string walletIdentifier, out ChainDefinition chain, out string wallet)
    {
        wallet = string.Empty;
        if (!registry.TryGet(chainKey, out chain!) || !chain.Enabled || chain.Family != ChainFamily.Solana || !chain.Capabilities.Faucet) return false;
        wallet = (walletIdentifier ?? string.Empty).Trim();
        return SolanaBase58.IsPublicKey(wallet);
    }

    private bool WalletMatchesSession(string wallet)
    {
        var sessionWallet = HttpContext.Session.GetString("WalletAddress") ?? User.FindFirst("wallet_address")?.Value ?? string.Empty;
        return string.Equals(wallet, sessionWallet, StringComparison.Ordinal);
    }

    public sealed record SolanaFaucetClaimRequest(string ChainKey, string WalletIdentifier);
}
