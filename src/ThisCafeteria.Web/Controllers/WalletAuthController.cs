using System.Globalization;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ThisCafeteria.Application.Configuration;
using Nethereum.Signer;
using Nethereum.Util;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Identity;
using ThisCafeteria.Infrastructure.Services;
using ThisCafeteria.Web.Models;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Route("api/wallet-auth")]
public sealed class WalletAuthController(
    IMemoryCache cache,
    IOptions<BlockchainNetworkOptions> chainOptions,
    IServiceProvider serviceProvider,
    ISqsMessagePublisher statusPublisher,
    ILogger<WalletAuthController> logger) : ControllerBase
{
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    private readonly BlockchainNetworkOptions _chain = chainOptions.Value;

    [HttpGet("status")]
    public async Task<IActionResult> GetLatestStatusAsync(
        [FromQuery] string walletAddress,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeAddress(walletAddress, out var address))
        {
            return BadRequest("A valid wallet address is required.");
        }

        var repository = serviceProvider.GetService<IWalletStatusEventRepository>();
        if (repository is null)
        {
            return NotFound("Wallet status storage is not configured. Set ConnectionStrings:DefaultConnection and run migrations.");
        }

        var latest = await repository.GetLatestForWalletAsync(address, cancellationToken);
        return latest is null ? NotFound() : Ok(MapStatus(latest));
    }

    [HttpPost("challenge")]
    public async Task<IActionResult> CreateChallenge([FromBody] WalletChallengeRequest request)
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
        await TryPublishStatusAsync(
            "wallet-login.challenge-created",
            "ChallengeCreated",
            address,
            request.WalletName);

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
            await TryPublishStatusAsync(
                "wallet-login.failed",
                "Failed",
                request.Address,
                request.WalletName,
                $"Wallet must be connected to {_chain.NetworkName}.");
            return BadRequest($"Wallet must be connected to {_chain.NetworkName}.");
        }

        if (!TryNormalizeAddress(request.Address, out var address))
        {
            await TryPublishStatusAsync(
                "wallet-login.failed",
                "Failed",
                request.Address,
                request.WalletName,
                "A valid wallet address is required.");
            return BadRequest("A valid wallet address is required.");
        }

        if (!cache.TryGetValue<WalletChallenge>(CacheKey(request.Nonce), out var challenge) || challenge is null)
        {
            await TryPublishStatusAsync(
                "wallet-login.failed",
                "Failed",
                address,
                request.WalletName,
                "The wallet login challenge expired.");
            return BadRequest("The wallet login challenge expired. Please try again.");
        }

        cache.Remove(CacheKey(request.Nonce));

        if (!AddressUtil.Current.AreAddressesTheSame(address, challenge.Address) ||
            request.Message != challenge.Message ||
            request.ChainId != challenge.ChainId)
        {
            await TryPublishStatusAsync(
                "wallet-login.failed",
                "Failed",
                address,
                request.WalletName,
                "The signed wallet login challenge does not match this session.");
            return BadRequest("The signed wallet login challenge does not match this session.");
        }

        var signer = new EthereumMessageSigner();
        var recoveredAddress = signer.EncodeUTF8AndEcRecover(request.Message, request.Signature);

        if (!AddressUtil.Current.AreAddressesTheSame(address, recoveredAddress))
        {
            await TryPublishStatusAsync(
                "wallet-login.failed",
                "Failed",
                address,
                request.WalletName,
                "The signature was not produced by the requested wallet.");
            return Unauthorized("The signature was not produced by the requested wallet.");
        }

        var checksumAddress = AddressUtil.Current.ConvertToChecksumAddress(address);
        var userManager = serviceProvider.GetService<UserManager<ApplicationUser>>();
        var signInManager = serviceProvider.GetService<SignInManager<ApplicationUser>>();

        if (userManager is not null && signInManager is not null)
        {
            var user = await FindOrCreateWalletUserAsync(userManager, checksumAddress);
            user.WalletAddress = checksumAddress;
            user.WalletChainId = _chain.ChainId;
            user.WalletVerifiedAt = DateTimeOffset.UtcNow;

            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                await TryPublishStatusAsync(
                    "wallet-login.failed",
                    "Failed",
                    checksumAddress,
                    request.WalletName,
                    "Could not update the wallet user.");
                return Problem("Could not update the wallet user.");
            }

            await signInManager.SignInAsync(user, isPersistent: true);
        }
        else
        {
            await SignInWalletSessionAsync(checksumAddress);
        }

        var statusResult = await RecordStatusAsync(
            "wallet-login.verified",
            "Verified",
            checksumAddress,
            request.WalletName);

        return Ok(new WalletVerifyResponse(
            true,
            checksumAddress,
            "/",
            statusResult.Stored,
            statusResult.Published,
            statusResult.AwsMessageId));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync()
    {
        var wallet = User.FindFirstValue("wallet_address") ??
            HttpContext.Session.GetString("WalletAddress") ??
            string.Empty;

        var signInManager = serviceProvider.GetService<SignInManager<ApplicationUser>>();
        if (signInManager is not null)
        {
            await signInManager.SignOutAsync();
        }
        else
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        HttpContext.Session.Remove("WalletAddress");

        if (!string.IsNullOrWhiteSpace(wallet))
        {
            await TryPublishStatusAsync(
                "wallet-login.logout",
                "LoggedOut",
                wallet);
        }

        return LocalRedirect("/");
    }

    private async Task<ApplicationUser> FindOrCreateWalletUserAsync(
        UserManager<ApplicationUser> userManager,
        string address)
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

    private async Task SignInWalletSessionAsync(string address)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, address),
            new(ClaimTypes.Name, address),
            new("wallet_address", address),
            new("wallet_chain_id", _chain.ChainId.ToString(CultureInfo.InvariantCulture)),
            new("wallet_network", _chain.NetworkName)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            IssuedUtc = issuedAt,
            ExpiresUtc = issuedAt.AddHours(8)
        };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
        HttpContext.Session.SetString("WalletAddress", address);
    }

    private async Task<bool> TryPublishStatusAsync(
        string eventName,
        string status,
        string walletAddress,
        string? walletName = null,
        string? reason = null)
    {
        var result = await RecordStatusAsync(eventName, status, walletAddress, walletName, reason);
        return result.Stored || result.Published;
    }

    private async Task<WalletStatusResult> RecordStatusAsync(
        string eventName,
        string status,
        string walletAddress,
        string? walletName = null,
        string? reason = null)
    {
        try
        {
            var occurredAtUtc = DateTimeOffset.UtcNow;
            var payload = new
            {
                eventName,
                chainId = _chain.ChainId,
                networkName = _chain.NetworkName,
                walletName,
                reason,
                userAgent = Request.Headers["User-Agent"].ToString(),
                remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            };
            var payloadJson = JsonSerializer.Serialize(payload);
            var message = new WalletStatusMessage(
                walletAddress,
                status,
                eventName,
                payload,
                occurredAtUtc);

            var repository = serviceProvider.GetService<IWalletStatusEventRepository>();
            var stored = false;
            WalletStatusEvent? statusEvent = null;
            if (repository is not null && TryNormalizeAddress(walletAddress, out var normalizedAddress))
            {
                statusEvent = new WalletStatusEvent
                {
                    WalletAddress = normalizedAddress,
                    Status = message.Status,
                    EventType = message.EventType,
                    PayloadJson = payloadJson,
                    CreatedAt = message.CreatedAt
                };

                await repository.AddAsync(statusEvent, HttpContext.RequestAborted);
                stored = true;
            }

            string? awsMessageId = null;
            try
            {
                awsMessageId = await statusPublisher.PublishAsync(message, HttpContext.RequestAborted);
                if (!string.IsNullOrWhiteSpace(awsMessageId) && repository is not null && statusEvent is not null)
                {
                    await repository.MarkPublishedToAwsAsync(
                        statusEvent.Id,
                        awsMessageId,
                        DateTimeOffset.UtcNow,
                        HttpContext.RequestAborted);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Wallet auth status was stored but could not be published to SQS. Status={Status}, WalletAddress={WalletAddress}",
                    status,
                    walletAddress);
            }

            return new WalletStatusResult(stored, !string.IsNullOrWhiteSpace(awsMessageId), awsMessageId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Could not publish wallet auth status {Status} for {WalletAddress}",
                status,
                walletAddress);
            return new WalletStatusResult(false, false, null);
        }
    }

    private static WalletStatusResponse MapStatus(WalletStatusEvent statusEvent) => new(
        statusEvent.Id,
        statusEvent.WalletAddress,
        statusEvent.Status,
        statusEvent.EventType,
        ParsePayload(statusEvent.PayloadJson),
        statusEvent.CreatedAt,
        statusEvent.PublishedToAwsAtUtc,
        statusEvent.AwsMessageId);

    private static JsonElement? ParsePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(payloadJson);
        return document.RootElement.Clone();
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
    private sealed record WalletStatusResult(bool Stored, bool Published, string? AwsMessageId);
}
