using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nethereum.Util;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.Web.Controllers;

[Route("staking")]
[IgnoreAntiforgeryToken]
public sealed class StakingController(
    ICoffeeWeb3Service web3Service,
    BlockchainNetworkOptions chain,
    AppDbContext dbContext) : Controller
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
    public Task<IActionResult> RecordStakeAsync(
        [FromBody] StakingTransactionRequest request,
        CancellationToken cancellationToken) =>
        RecordStakingTransactionAsync(request, StakingTransactionType.Stake, cancellationToken);

    [HttpPost("api/record-unstake")]
    public Task<IActionResult> RecordUnstakeAsync(
        [FromBody] StakingTransactionRequest request,
        CancellationToken cancellationToken) =>
        RecordStakingTransactionAsync(request, StakingTransactionType.Unstake, cancellationToken);

    [HttpPost("save-wallet-session")]
    public IActionResult SaveWalletSession([FromBody] SaveWalletSessionRequest request)
    {
        if (request.ChainId != chain.ChainId)
        {
            return BadRequest($"Connect MetaMask to {chain.NetworkName} before starting an allocation.");
        }

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

        if (!TryResolveCurrentWallet(out var sessionWallet))
        {
            return Unauthorized("Connect or sign in with your wallet before recording staking activity.");
        }

        if (!AddressUtil.Current.AreAddressesTheSame(wallet, sessionWallet))
        {
            return BadRequest("The connected wallet does not match the staking session wallet.");
        }

        if (!StakingAmountRules.IsValidStakeAmount(request.Amount))
        {
            return BadRequest("Staking amount must be greater than zero.");
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

        var verified = await web3Service.VerifyStakingTransactionAsync(
            transactionHash,
            wallet,
            request.Amount,
            transactionType,
            cancellationToken);

        if (!verified)
        {
            return BadRequest("Staking transaction could not be verified on-chain.");
        }

        var entry = new StakingLedgerEntry
        {
            WalletAddress = wallet,
            ActionType = transactionType == StakingTransactionType.Stake ? "stake" : "unstake",
            Amount = request.Amount,
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

    private bool IsStakingConfigured() =>
        IsConfiguredAddress(chain.EffectivePaymentTokenContract) &&
        IsConfiguredAddress(chain.StakingPoolContract);

    private bool TryResolveCurrentWallet(out string wallet)
    {
        wallet = string.Empty;

        var candidates = new[]
        {
            User.FindFirst("wallet_address")?.Value,
            User.Identity?.Name,
            HttpContext.Session.GetString(WalletSessionKey)
        };

        foreach (var candidate in candidates)
        {
            if (TryNormalizeWallet(candidate, out wallet))
            {
                return true;
            }
        }

        return false;
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

    public sealed record SaveWalletSessionRequest(string WalletAddress, int ChainId);

    public sealed record StakingTransactionRequest(
        string WalletAddress,
        decimal Amount,
        string TransactionHash);
}
