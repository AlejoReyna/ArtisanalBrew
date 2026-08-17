using ThisCafeteria.Application.Services.Wallet;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ThisCafeteria.Application.Configuration;
using Nethereum.Util;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Web.Models;
using ThisCafeteria.Web.Services.Wallet;
using ThisCafeteria.Web.Services.Blockchain;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Route("api/wallet-auth")]
[EnableRateLimiting("sensitive")]
public sealed class WalletAuthController(
    IWalletChallengeService challengeService,
    ISolanaWalletChallengeService solanaChallengeService,
    IChainRegistry chainRegistry,
    ISelectedChainAccessor selectedChainAccessor,
    IOptions<BlockchainNetworkOptions> chainOptions,
    IServiceProvider serviceProvider,
    IUnitOfWork unitOfWork,
    ISqsMessagePublisher statusPublisher,
    ILogger<WalletAuthController> logger) : ControllerBase
{
    private readonly BlockchainNetworkOptions _chain = chainOptions.Value;
    private readonly IChainRegistry _chainRegistry = chainRegistry;
    private readonly ISelectedChainAccessor _selectedChainAccessor = selectedChainAccessor;

    [HttpPost("solana/challenge")]
    public async Task<IActionResult> CreateSolanaChallenge([FromBody] SolanaWalletChallengeRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetSolanaChain(request.ChainKey, out var chain) || !chain.Capabilities.WalletLogin)
            return StatusCode(StatusCodes.Status409Conflict, "Solana wallet login is not enabled for this chain.");
        var origin = $"{Request.Scheme}://{Request.Host}";
        try
        {
            var challenge = await solanaChallengeService.CreateAsync(request.Address, chain, origin, cancellationToken);
            return Ok(new SolanaWalletChallengeResponse(challenge.Message, challenge.Nonce, chain.Key, chain.SolanaCluster!, challenge.IssuedAt, challenge.ExpiresAt));
        }
        catch (ArgumentException) { return BadRequest("A valid Solana public key is required."); }
    }

    [HttpPost("solana/verify")]
    public async Task<IActionResult> VerifySolanaAsync([FromBody] SolanaWalletVerifyRequest request)
    {
        if (!TryGetSolanaChain(request.ChainKey, out var chain) || !chain.Capabilities.WalletLogin)
            return StatusCode(StatusCodes.Status409Conflict, "Solana wallet login is not enabled for this chain.");
        var origin = $"{Request.Scheme}://{Request.Host}";
        await using var authenticationTransaction = await unitOfWork.BeginTransactionAsync(HttpContext.RequestAborted);
        var error = await solanaChallengeService.VerifyAsync(request.Address, request.Message, request.Nonce, chain.Key, origin, request.Signature, HttpContext.RequestAborted);
        if (error != SolanaChallengeVerificationError.None)
            return error == SolanaChallengeVerificationError.InvalidSignature ? Unauthorized("The Solana signature is invalid.") : BadRequest("The Solana login challenge is expired or does not match.");

        var identityAccounts = serviceProvider.GetService<IIdentityAccountService>();
        IdentityAccount? authenticatedAccount = null;
        if (identityAccounts is not null)
        {
            var result = await identityAccounts.FindOrCreateAndLinkWalletAsync(
                request.Address, null, request.WalletName, chain.FamilyName, HttpContext.RequestAborted);
            if (!result.Succeeded)
            {
                return result.WalletAlreadyLinkedToAnotherAccount
                    ? Conflict("This Solana wallet identity is already verified for another user.")
                    : Problem(result.Error ?? "Could not update the Solana wallet identity.");
            }
            authenticatedAccount = result.Account;
        }
        await authenticationTransaction.CommitAsync(HttpContext.RequestAborted);
        if (authenticatedAccount is not null && identityAccounts is not null) await identityAccounts.SignInAsync(authenticatedAccount.Id, isPersistent: true);
        else await SignInWalletSessionAsync(request.Address, chain);
        return Ok(new WalletVerifyResponse(true, request.Address, "/profile?tour=true", false, false, null));
    }

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
        var selectedChain = ResolveSelectedEvmChain(request.ChainKey);
        if (selectedChain is null)
        {
            return BadRequest("Select an enabled EVM network before connecting an EVM wallet.");
        }

        if (!TryNormalizeAddress(request.Address, out var address))
        {
            return BadRequest("A valid wallet address is required.");
        }

        var origin = $"{Request.Scheme}://{Request.Host}";
        var challenge = challengeService.CreateChallenge(
            address,
            selectedChain.EvmChainId!.Value,
            selectedChain.DisplayName,
            origin,
            selectedChain.Key,
            selectedChain.FamilyName);
        await TryPublishStatusAsync(
            "wallet-login.challenge-created",
            "ChallengeCreated",
            address,
            request.WalletName);

        return Ok(new WalletChallengeResponse(
            challenge.Message,
            challenge.Nonce,
            selectedChain.EvmChainId.Value,
            selectedChain.EvmChainIdHex!,
            selectedChain.DisplayName,
            selectedChain.PublicRpcUrl,
            ExplorerBaseUrl(selectedChain),
            selectedChain.NativeCurrencyName,
            selectedChain.NativeCurrencySymbol,
            selectedChain.NativeCurrencyDecimals,
            selectedChain.Key,
            selectedChain.FamilyName));
    }

    [HttpPost("verify")]
    public async Task<IActionResult> VerifyAsync([FromBody] WalletVerifyRequest request)
    {
        var selectedChain = ResolveSelectedEvmChain(request.ChainKey);
        if (selectedChain is null || request.ChainId != selectedChain.EvmChainId)
        {
            await TryPublishStatusAsync(
                "wallet-login.failed",
                "Failed",
                request.Address,
                request.WalletName,
                $"Wallet must be connected to {selectedChain?.DisplayName ?? "the selected EVM network"}.");
            return BadRequest($"Wallet must be connected to {selectedChain?.DisplayName ?? "the selected EVM network"}.");
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

        var verificationError = challengeService.Verify(
            address, request.Message, request.Nonce, request.ChainId, request.Signature, out _, selectedChain.Key, selectedChain.FamilyName);

        if (verificationError == WalletChallengeVerificationError.Expired)
        {
            await TryPublishStatusAsync(
                "wallet-login.failed",
                "Failed",
                address,
                request.WalletName,
                "The wallet login challenge expired.");
            return BadRequest("The wallet login challenge expired. Please try again.");
        }

        if (verificationError == WalletChallengeVerificationError.Mismatch)
        {
            await TryPublishStatusAsync(
                "wallet-login.failed",
                "Failed",
                address,
                request.WalletName,
                "The signed wallet login challenge does not match this session.");
            return BadRequest("The signed wallet login challenge does not match this session.");
        }

        if (verificationError == WalletChallengeVerificationError.InvalidSignature)
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
        var identityAccounts = serviceProvider.GetService<IIdentityAccountService>();

        if (identityAccounts is not null)
        {
            var result = await identityAccounts.FindOrCreateAndLinkWalletAsync(
                checksumAddress,
                selectedChain.EvmChainId!.Value,
                request.WalletName,
                selectedChain.FamilyName,
                HttpContext.RequestAborted);
            if (!result.Succeeded)
            {
                await TryPublishStatusAsync(
                    "wallet-login.failed",
                    "Failed",
                    checksumAddress,
                    request.WalletName,
                    result.Error ?? "Could not update the wallet user.");
                return result.WalletAlreadyLinkedToAnotherAccount
                    ? Conflict("This wallet identity is already verified for another user.")
                    : Problem(result.Error ?? "Could not update the wallet user.");
            }

            await identityAccounts.SignInAsync(result.Account!.Id, isPersistent: true);
        }
        else
        {
            await SignInWalletSessionAsync(checksumAddress, selectedChain);
        }

        var statusResult = await RecordStatusAsync(
            "wallet-login.verified",
            "Verified",
            checksumAddress,
            request.WalletName);

        return Ok(new WalletVerifyResponse(
            true,
            checksumAddress,
            "/profile?tour=true",
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

        var identityAccounts = serviceProvider.GetService<IIdentityAccountService>();
        if (identityAccounts is not null)
        {
            await identityAccounts.SignOutAsync();
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

    private async Task SignInWalletSessionAsync(string address, ChainDefinition chain)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, address),
            new(ClaimTypes.Name, address),
            new("wallet_address", address),
            new("wallet_chain_id", chain.EvmChainId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            new("wallet_network", chain.DisplayName),
            new("wallet_chain_key", chain.Key),
            new("wallet_family", chain.FamilyName)
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
        HttpContext.Session.SetString("WalletChainKey", chain.Key);
    }

    private ChainDefinition? ResolveSelectedEvmChain(string? requestedChainKey)
    {
        var key = string.IsNullOrWhiteSpace(requestedChainKey)
            ? _selectedChainAccessor.SelectedChainKey
            : requestedChainKey.Trim();
        return _chainRegistry.TryGet(key, out var chain)
            && chain.Enabled
            && chain.Family == ChainFamily.Evm
            && chain.Capabilities.WalletLogin
            ? chain
            : null;
    }

    private bool TryGetSolanaChain(string chainKey, out ChainDefinition chain) =>
        _chainRegistry.TryGet(chainKey, out chain!) && chain.Enabled && chain.Family == ChainFamily.Solana;


    private static string ExplorerBaseUrl(ChainDefinition chain)
    {
        var template = chain.ExplorerAddressTemplate;
        var marker = template.IndexOf("/address/", StringComparison.OrdinalIgnoreCase);
        return marker > 0 ? template[..marker] : template;
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

    private sealed record WalletStatusResult(bool Stored, bool Published, string? AwsMessageId);
}
