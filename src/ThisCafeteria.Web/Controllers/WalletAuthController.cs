using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Nethereum.Signer;
using Nethereum.Util;
using ThisCafeteria.Infrastructure.Identity;
using ThisCafeteria.Web.Configuration;
using ThisCafeteria.Web.Models;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Route("api/wallet-auth")]
public sealed class WalletAuthController(
    IMemoryCache cache,
    IOptions<BnbTestnetOptions> chainOptions,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : ControllerBase
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    private readonly BnbTestnetOptions _chain = chainOptions.Value;

    [HttpPost("challenge")]
    public IActionResult CreateChallenge([FromBody] WalletChallengeRequest request)
    {
        if (!TryNormalizeAddress(request.Address, out var address))
        {
            return BadRequest("A valid wallet address is required.");
        }

        var nonceBytes = RandomNumberGenerator.GetBytes(16);
        var nonce = Convert.ToHexString(nonceBytes).ToLowerInvariant();
        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.Add(ChallengeLifetime);
        var origin = $"{Request.Scheme}://{Request.Host}";
        var message = string.Join('\n',
            "ThisCafeteria wallet login",
            string.Empty,
            $"Address: {address}",
            $"Chain ID: {_chain.ChainId.ToString(CultureInfo.InvariantCulture)}",
            $"Network: {_chain.NetworkName}",
            $"URI: {origin}",
            "Version: 1",
            $"Nonce: {nonce}",
            $"Issued At: {issuedAt:O}",
            $"Expiration Time: {expiresAt:O}");

        cache.Set(CacheKey(nonce), new WalletChallenge(address, message, _chain.ChainId), expiresAt);

        return Ok(new WalletChallengeResponse(
            message,
            nonce,
            _chain.ChainId,
            _chain.NetworkName,
            _chain.RpcUrl,
            _chain.ExplorerUrl));
    }

    [HttpPost("verify")]
    public async Task<IActionResult> VerifyAsync([FromBody] WalletVerifyRequest request)
    {
        if (request.ChainId != _chain.ChainId)
        {
            return BadRequest($"Wallet must be connected to {_chain.NetworkName}.");
        }

        if (!TryNormalizeAddress(request.Address, out var address))
        {
            return BadRequest("A valid wallet address is required.");
        }

        if (!cache.TryGetValue<WalletChallenge>(CacheKey(request.Nonce), out var challenge) || challenge is null)
        {
            return BadRequest("The wallet login challenge expired. Please try again.");
        }

        cache.Remove(CacheKey(request.Nonce));

        if (!AddressUtil.Current.AreAddressesTheSame(address, challenge.Address) ||
            request.Message != challenge.Message ||
            request.ChainId != challenge.ChainId)
        {
            return BadRequest("The signed wallet login challenge does not match this session.");
        }

        var signer = new EthereumMessageSigner();
        var recoveredAddress = signer.EncodeUTF8AndEcRecover(request.Message, request.Signature);

        if (!AddressUtil.Current.AreAddressesTheSame(address, recoveredAddress))
        {
            return Unauthorized("The signature was not produced by the requested wallet.");
        }

        var user = await FindOrCreateWalletUserAsync(address);
        user.WalletAddress = AddressUtil.Current.ConvertToChecksumAddress(address);
        user.WalletChainId = _chain.ChainId;
        user.WalletVerifiedAt = DateTimeOffset.UtcNow;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Problem("Could not update the wallet user.");
        }

        await signInManager.SignInAsync(user, isPersistent: true);

        return Ok(new WalletVerifyResponse(true, user.WalletAddress, "/"));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync()
    {
        await signInManager.SignOutAsync();
        return LocalRedirect("/");
    }

    private async Task<ApplicationUser> FindOrCreateWalletUserAsync(string address)
    {
        var checksumAddress = AddressUtil.Current.ConvertToChecksumAddress(address);
        var user = userManager.Users.FirstOrDefault(user => user.WalletAddress == checksumAddress);
        if (user is not null)
        {
            return user;
        }

        user = await userManager.FindByNameAsync(checksumAddress);
        if (user is not null)
        {
            return user;
        }

        var syntheticEmail = CreateSyntheticWalletEmail(checksumAddress);
        user = await userManager.FindByEmailAsync(syntheticEmail);
        if (user is not null)
        {
            return user;
        }

        user = new ApplicationUser
        {
            UserName = checksumAddress,
            Email = syntheticEmail,
            EmailConfirmed = true,
            WalletAddress = checksumAddress,
            WalletChainId = _chain.ChainId,
            WalletVerifiedAt = DateTimeOffset.UtcNow
        };

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(error => error.Description)));
        }

        return user;
    }

    private static string CreateSyntheticWalletEmail(string address) =>
        $"{address.ToLowerInvariant()}@wallet.thiscafeteria.local";

    private static bool TryNormalizeAddress(string? address, out string normalizedAddress)
    {
        normalizedAddress = string.Empty;
        if (string.IsNullOrWhiteSpace(address) || !AddressUtil.Current.IsValidEthereumAddressHexFormat(address))
        {
            return false;
        }

        normalizedAddress = AddressUtil.Current.ConvertToChecksumAddress(address);
        return true;
    }

    private static string CacheKey(string nonce) => $"wallet-auth:{nonce}";

    private sealed record WalletChallenge(string Address, string Message, int ChainId);
}
