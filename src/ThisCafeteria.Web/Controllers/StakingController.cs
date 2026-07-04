using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nethereum.Util;
using ThisCafeteria.Infrastructure.Identity;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Web.Services.Wallet;

namespace ThisCafeteria.Web.Controllers;

[Route("staking")]
public sealed class StakingController(
    ICoffeeWeb3Service web3Service,
    BlockchainNetworkOptions chain,
    AppDbContext dbContext,
    IServiceProvider serviceProvider,
    IWalletChallengeService challengeService) : Controller
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

        if (!AddressUtil.Current.IsValidEthereumAddressHexFormat(wallet))
        {
            return BadRequest("A valid wallet address is required.");
        }

        var checksum = AddressUtil.Current.ConvertToChecksumAddress(wallet);
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

        if (!TryNormalizeWallet(request.WalletAddress, out var wallet))
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

        if (!TryNormalizeTransactionHash(request.TransactionHash, out var transactionHash))
        {
            return BadRequest("A valid staking transaction hash is required.");
        }

        bool transactionExists;
        try
        {
            transactionExists = await StakingTransactionExistsAsync(transactionHash, cancellationToken);
        }
        catch (Exception exception) when (IsMissingStakingLedgerTable(exception))
        {
            return BadRequest("The staking ledger database migration has not been applied yet. Restart the app or apply migrations before staking.");
        }

        if (transactionExists)
        {
            return Conflict("This staking transaction has already been recorded.");
        }

        var verification = await web3Service.VerifyStakingTransactionAsync(
            transactionHash,
            wallet,
            expectedAmount: null,
            StakingTransactionType.Claim,
            cancellationToken);

        if (verification.Status == TransactionVerificationStatus.PendingConfirmations)
        {
            return PendingConfirmationsResult(verification);
        }

        if (!verification.Verified)
        {
            return BadRequest("Claim transaction could not be verified on-chain.");
        }

        var entry = new StakingLedgerEntry
        {
            WalletAddress = wallet,
            ActionType = "claim",
            Amount = verification.Amount,
            TransactionHash = transactionHash,
            ChainId = chain.ChainId,
            NetworkName = chain.NetworkName,
            PaymentTokenContract = chain.CoffeeCoinContract,
            StakingPoolContract = chain.StakingPoolContract,
            ExplorerUrl = BuildExplorerTransactionUrl(transactionHash),
            RecordedAtUtc = DateTime.UtcNow
        };

        dbContext.StakingLedgerEntries.Add(entry);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsMissingStakingLedgerTable(exception))
        {
            return BadRequest("The staking ledger database migration has not been applied yet. Restart the app or apply migrations before staking.");
        }
        catch (DbUpdateException)
        {
            return Conflict("This staking transaction has already been recorded.");
        }
        catch (Exception exception) when (IsMissingStakingLedgerTable(exception))
        {
            return BadRequest("The staking ledger database migration has not been applied yet. Restart the app or apply migrations before staking.");
        }

        return Ok(new
        {
            success = true,
            entry.TransactionHash,
            entry.ActionType,
            entry.Amount,
            entry.ExplorerUrl
        });
    }

    [HttpPost("save-wallet-session")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveWalletSessionAsync([FromBody] SaveWalletSessionRequest request)
    {
        if (request.ChainId != chain.ChainId)
        {
            return BadRequest($"Connect your wallet to {chain.NetworkName} before starting an allocation.");
        }

        if (!TryNormalizeWallet(request.WalletAddress, out var wallet))
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

        if (!TryNormalizeWallet(request.WalletAddress, out var wallet))
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

        if (!TryNormalizeTransactionHash(request.TransactionHash, out var transactionHash))
        {
            return BadRequest("A valid staking transaction hash is required.");
        }

        bool transactionExists;
        try
        {
            transactionExists = await StakingTransactionExistsAsync(transactionHash, cancellationToken);
        }
        catch (Exception exception) when (IsMissingStakingLedgerTable(exception))
        {
            return BadRequest("The staking ledger database migration has not been applied yet. Restart the app or apply migrations before staking.");
        }

        if (transactionExists)
        {
            return Conflict("This staking transaction has already been recorded.");
        }

        var verification = await web3Service.VerifyStakingTransactionAsync(
            transactionHash,
            wallet,
            request.Amount,
            transactionType,
            cancellationToken);

        if (verification.Status == TransactionVerificationStatus.PendingConfirmations)
        {
            return PendingConfirmationsResult(verification);
        }

        if (!verification.Verified)
        {
            return BadRequest("Staking transaction could not be verified on-chain.");
        }

        var entry = new StakingLedgerEntry
        {
            WalletAddress = wallet,
            ActionType = transactionType == StakingTransactionType.Stake ? "stake" : "unstake",
            Amount = verification.Amount,
            TransactionHash = transactionHash,
            ChainId = chain.ChainId,
            NetworkName = chain.NetworkName,
            PaymentTokenContract = chain.EffectivePaymentTokenContract,
            StakingPoolContract = chain.StakingPoolContract,
            ExplorerUrl = BuildExplorerTransactionUrl(transactionHash),
            RecordedAtUtc = DateTime.UtcNow
        };

        dbContext.StakingLedgerEntries.Add(entry);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsMissingStakingLedgerTable(exception))
        {
            return BadRequest("The staking ledger database migration has not been applied yet. Restart the app or apply migrations before staking.");
        }
        catch (DbUpdateException)
        {
            return Conflict("This staking transaction has already been recorded.");
        }
        catch (Exception exception) when (IsMissingStakingLedgerTable(exception))
        {
            return BadRequest("The staking ledger database migration has not been applied yet. Restart the app or apply migrations before staking.");
        }

        return Ok(new
        {
            success = true,
            entry.TransactionHash,
            entry.ActionType,
            entry.Amount,
            entry.ExplorerUrl
        });
    }

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
        IsConfiguredAddress(chain.EffectivePaymentTokenContract) &&
        IsConfiguredAddress(chain.StakingPoolContract);

    private async Task<bool> IsWalletAlreadyAuthenticatedAsync(string wallet)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var claimWallet = User.FindFirst("wallet_address")?.Value ?? User.Identity?.Name;
        if (TryNormalizeWallet(claimWallet, out var normalizedClaimWallet) &&
            AddressUtil.Current.AreAddressesTheSame(normalizedClaimWallet, wallet))
        {
            return true;
        }

        var userManager = serviceProvider.GetService<UserManager<ApplicationUser>>();
        if (userManager is null)
        {
            return false;
        }

        var user = await userManager.GetUserAsync(User);
        return TryNormalizeWallet(user?.WalletAddress, out var normalizedUserWallet) &&
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

        var userManager = serviceProvider.GetService<UserManager<ApplicationUser>>();
        if (User.Identity?.IsAuthenticated == true && userManager is not null)
        {
            var user = await userManager.GetUserAsync(User);
            if (!string.IsNullOrWhiteSpace(user?.WalletAddress))
            {
                candidates.Insert(0, user.WalletAddress);
            }
        }

        foreach (var candidate in candidates)
        {
            if (TryNormalizeWallet(candidate, out var wallet))
            {
                return (true, wallet);
            }
        }

        return (false, string.Empty);
    }

    private Task<bool> StakingTransactionExistsAsync(
        string transactionHash,
        CancellationToken cancellationToken) =>
        dbContext.StakingLedgerEntries.AnyAsync(
            entry => entry.TransactionHash == transactionHash,
            cancellationToken);

    private string BuildExplorerTransactionUrl(string transactionHash)
    {
        var explorer = chain.ExplorerUrl?.Trim();
        return string.IsNullOrWhiteSpace(explorer)
            ? string.Empty
            : $"{explorer.TrimEnd('/')}/tx/{transactionHash}";
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

    private static bool IsConfiguredAddress(string? address) =>
        !string.IsNullOrWhiteSpace(address) &&
        address.Length == 42 &&
        address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
        address[2..].All(Uri.IsHexDigit) &&
        !address.Equals("0x0000000000000000000000000000000000000000", StringComparison.OrdinalIgnoreCase);

    private static bool IsMissingStakingLedgerTable(Exception exception) =>
        exception.Message.Contains("42P01", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("StakingLedgerEntries", StringComparison.OrdinalIgnoreCase) ||
        (exception.InnerException is not null && IsMissingStakingLedgerTable(exception.InnerException));

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
