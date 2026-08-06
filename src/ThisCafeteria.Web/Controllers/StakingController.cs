using Microsoft.AspNetCore.Mvc;
using Nethereum.Util;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Web.Services.Wallet;
using System.Security.Claims;

namespace ThisCafeteria.Web.Controllers;

[Route("staking")]
public sealed class StakingController(
    ICoffeeWeb3Service web3Service,
    IStakingLedgerService ledgerService,
    BlockchainNetworkOptions chain,
    IIdentityAccountService identityAccounts,
    IWalletChallengeService challengeService) : Controller
{
    private const string WalletSessionKey = "WalletAddress";

    [HttpGet("api/coffee-balance")]
    public async Task<IActionResult> GetCoffeeBalanceAsync(
        [FromQuery] string walletAddress,
        CancellationToken cancellationToken)
    {
        if (!WalletAddressRules.TryNormalizeWallet(walletAddress, out var checksum))
        {
            return BadRequest("A valid wallet address is required.");
        }

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

        if (!WalletAddressRules.TryNormalizeWallet(wallet, out var checksum))
        {
            return BadRequest("A valid wallet address is required.");
        }

        var model = await web3Service.GetDashboardDataAsync(checksum, cancellationToken);
        return Ok(model);
    }

    [HttpGet("api/position")]
    public async Task<IActionResult> GetPositionAsync(
        [FromQuery] string? walletAddress,
        CancellationToken cancellationToken)
    {
        var wallet = ResolveWalletAddress(walletAddress);
        if (string.IsNullOrWhiteSpace(wallet))
        {
            return Ok(new CoffeeDashboardModel());
        }

        if (!WalletAddressRules.TryNormalizeWallet(wallet, out var checksum))
        {
            return BadRequest("A valid wallet address is required.");
        }

        var model = await web3Service.GetDashboardDataAsync(checksum, cancellationToken);
        return Ok(model);
    }

    [HttpPost("api/record-stake")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RecordStakeAsync(
        [FromBody] StakingTransactionRequest request,
        CancellationToken cancellationToken) =>
        RecordStakingTransactionAsync(request, StakingTransactionType.Stake, cancellationToken);

    [HttpPost("api/record-unstake")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RecordUnstakeAsync(
        [FromBody] StakingTransactionRequest request,
        CancellationToken cancellationToken) =>
        RecordStakingTransactionAsync(request, StakingTransactionType.Unstake, cancellationToken);

    [HttpPost("api/record-claim")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordClaimAsync(
        [FromBody] StakingClaimRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsStakingConfigured())
        {
            return BadRequest("Configure a payment token and staking pool contract before staking.");
        }

        if (!WalletAddressRules.TryNormalizeWallet(request.WalletAddress, out var wallet))
        {
            return BadRequest("A valid wallet address is required.");
        }

        var (foundSessionWallet, sessionWallet) = await TryResolveCurrentWalletAsync();
        if (!foundSessionWallet)
        {
            return Unauthorized("Connect or sign in with your wallet before recording staking activity.");
        }

        if (!AddressUtil.Current.AreAddressesTheSame(wallet, sessionWallet))
        {
            return BadRequest("The connected wallet does not match the staking session wallet.");
        }

        if (!WalletAddressRules.TryNormalizeTransactionHash(request.TransactionHash, out var transactionHash))
        {
            return BadRequest("A valid staking transaction hash is required.");
        }

        var result = await ledgerService.RecordAsync(
            chain,
            wallet,
            transactionHash,
            StakingTransactionType.Claim,
            expectedAmount: null,
            cancellationToken);

        return ToActionResult(result, "Claim transaction could not be verified on-chain.");
    }

    [HttpPost("save-wallet-session")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveWalletSessionAsync([FromBody] SaveWalletSessionRequest request)
    {
        if (request.ChainId != chain.ChainId)
        {
            return BadRequest($"Connect your wallet to {chain.NetworkName} before starting an allocation.");
        }

        if (!WalletAddressRules.TryNormalizeWallet(request.WalletAddress, out var wallet))
        {
            return BadRequest("A valid wallet address is required.");
        }

        if (!await IsWalletAlreadyAuthenticatedAsync(wallet))
        {
            if (string.IsNullOrWhiteSpace(request.Signature) ||
                string.IsNullOrWhiteSpace(request.Message) ||
                string.IsNullOrWhiteSpace(request.Nonce))
            {
                return Unauthorized("Sign the wallet ownership challenge before connecting for staking.");
            }

            var verificationError = challengeService.Verify(
                wallet, request.Message, request.Nonce, request.ChainId, request.Signature, out _);

            if (verificationError != WalletChallengeVerificationError.None)
            {
                return Unauthorized("Wallet ownership could not be verified.");
            }
        }

        HttpContext.Session.SetString(WalletSessionKey, wallet);
        return Ok();
    }

    [HttpPost("clear-wallet-session")]
    [ValidateAntiForgeryToken]
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

    private async Task<IActionResult> RecordStakingTransactionAsync(
        StakingTransactionRequest request,
        StakingTransactionType transactionType,
        CancellationToken cancellationToken)
    {
        if (!IsStakingConfigured())
        {
            return BadRequest("Configure a payment token and staking pool contract before staking.");
        }

        if (!WalletAddressRules.TryNormalizeWallet(request.WalletAddress, out var wallet))
        {
            return BadRequest("A valid wallet address is required.");
        }

        var (foundSessionWallet, sessionWallet) = await TryResolveCurrentWalletAsync();
        if (!foundSessionWallet)
        {
            return Unauthorized("Connect or sign in with your wallet before recording staking activity.");
        }

        if (!AddressUtil.Current.AreAddressesTheSame(wallet, sessionWallet))
        {
            return BadRequest("The connected wallet does not match the staking session wallet.");
        }

        if (!IsValidAmountForType(transactionType, request.Amount))
        {
            return BadRequest(transactionType == StakingTransactionType.Stake
                ? "Staking amount must be greater than zero."
                : "Unstake amount must be greater than zero.");
        }

        if (!WalletAddressRules.TryNormalizeTransactionHash(request.TransactionHash, out var transactionHash))
        {
            return BadRequest("A valid staking transaction hash is required.");
        }

        var result = await ledgerService.RecordAsync(
            chain,
            wallet,
            transactionHash,
            transactionType,
            request.Amount,
            cancellationToken);

        return ToActionResult(result, "Staking transaction could not be verified on-chain.");
    }

    /// <summary>Maps a record outcome onto the response shape these endpoints have always returned.</summary>
    private IActionResult ToActionResult(StakingRecordResult result, string verificationFailureMessage) =>
        result.Status switch
        {
            StakingRecordStatus.Recorded => Ok(new
            {
                success = true,
                result.Entry!.TransactionHash,
                result.Entry.ActionType,
                result.Entry.Amount,
                result.Entry.ExplorerUrl
            }),
            StakingRecordStatus.AlreadyRecorded => Conflict("This staking transaction has already been recorded."),
            StakingRecordStatus.PendingConfirmations => PendingConfirmationsResult(result.Verification!),
            _ => BadRequest(verificationFailureMessage)
        };

    private IActionResult PendingConfirmationsResult(StakingVerificationResult verification) =>
        StatusCode(StatusCodes.Status202Accepted, new
        {
            success = false,
            status = "pending_confirmations",
            confirmations = verification.Confirmations,
            requiredConfirmations = verification.RequiredConfirmations
        });

    /// <summary>
    /// Stake amounts are validated against StakingAmountRules.IsValidStakeAmount (amount &gt; 0).
    /// Unstake amounts deliberately are NOT checked against StakingAmountRules.IsValidUnstakeAmount's
    /// staked-balance ceiling: by the time this endpoint runs, the unstake transaction has already been
    /// mined, so GetStakedPaymentTokenBalanceAsync would return the POST-unstake balance. Comparing the
    /// requested amount against that balance would reject legitimate full unstakes (post-balance can be
    /// 0 even for a perfectly valid withdrawal). VerifyStakingTransactionAsync already proves, via the
    /// decoded on-chain Transfer event, that the pool contract itself paid out exactly this amount -
    /// that is the real correctness guarantee, not a second, stale balance check here.
    /// </summary>
    private static bool IsValidAmountForType(StakingTransactionType transactionType, decimal amount) =>
        transactionType == StakingTransactionType.Stake
            ? StakingAmountRules.IsValidStakeAmount(amount)
            : amount > 0m;

    private bool IsStakingConfigured() =>
        WalletAddressRules.IsConfiguredAddress(chain.EffectivePaymentTokenContract) &&
        WalletAddressRules.IsConfiguredAddress(chain.StakingPoolContract);

    private async Task<bool> IsWalletAlreadyAuthenticatedAsync(string wallet)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var claimWallet = User.FindFirst("wallet_address")?.Value ?? User.Identity?.Name;
        if (WalletAddressRules.TryNormalizeWallet(claimWallet, out var normalizedClaimWallet) &&
            AddressUtil.Current.AreAddressesTheSame(normalizedClaimWallet, wallet))
        {
            return true;
        }

        var user = await FindCurrentAccountAsync();
        return WalletAddressRules.TryNormalizeWallet(user?.WalletAddress, out var normalizedUserWallet) &&
            AddressUtil.Current.AreAddressesTheSame(normalizedUserWallet, wallet);
    }

    private async Task<(bool Found, string Wallet)> TryResolveCurrentWalletAsync()
    {
        var candidates = new List<string?>
        {
            User.FindFirst("wallet_address")?.Value,
            User.Identity?.Name,
            HttpContext.Session.GetString(WalletSessionKey)
        };

        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await FindCurrentAccountAsync();
            if (!string.IsNullOrWhiteSpace(user?.WalletAddress))
            {
                candidates.Insert(0, user.WalletAddress);
            }
        }

        foreach (var candidate in candidates)
        {
            if (WalletAddressRules.TryNormalizeWallet(candidate, out var wallet))
            {
                return (true, wallet);
            }
        }

        return (false, string.Empty);
    }

    private Task<IdentityAccount?> FindCurrentAccountAsync()
    {
        var accountId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(accountId)
            ? Task.FromResult<IdentityAccount?>(null)
            : identityAccounts.FindByIdAsync(accountId, HttpContext.RequestAborted);
    }

    public sealed record SaveWalletSessionRequest(
        string WalletAddress,
        int ChainId,
        string? Signature = null,
        string? Message = null,
        string? Nonce = null);

    public sealed record StakingTransactionRequest(
        string WalletAddress,
        decimal Amount,
        string TransactionHash);

    public sealed record StakingClaimRequest(
        string WalletAddress,
        string TransactionHash);
}
