using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nethereum.Util;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;
using ThisCafeteria.Web.Services.Blockchain;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Route("staking/api/liquid")]
public sealed class LiquidStakingController(
    ILiquidStakingGateway gateway,
    IChainRegistry registry,
    ISelectedChainAccessor selectedChain,
    AppDbContext dbContext) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] string chainKey, [FromQuery] string walletIdentifier, CancellationToken cancellationToken)
    {
        if (!TryGetChain(chainKey, out var chain) || !TryNormalizeWallet(chain, walletIdentifier, out var wallet)) return BadRequest("A valid enabled chain and wallet are required.");
        if (!WalletMatchesSession(chain, wallet)) return Unauthorized("Connect the wallet selected for this session.");
        return Ok(await gateway.GetDashboardAsync(chainKey, wallet, cancellationToken));
    }

    [HttpPost("record-deposit")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RecordDeposit([FromBody] LiquidTransactionRequest request, CancellationToken cancellationToken) => Record(request, LiquidStakingOperation.Deposit, cancellationToken);

    [HttpPost("record-redeem")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RecordRedeem([FromBody] LiquidTransactionRequest request, CancellationToken cancellationToken) => Record(request, LiquidStakingOperation.Redeem, cancellationToken);

    [HttpPost("record-claim")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RecordClaim([FromBody] LiquidTransactionRequest request, CancellationToken cancellationToken) => Record(request, LiquidStakingOperation.Claim, cancellationToken);

    [HttpPost("record-reward-funding")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RecordRewardFunding([FromBody] LiquidTransactionRequest request, CancellationToken cancellationToken) => Record(request, LiquidStakingOperation.RewardFunding, cancellationToken);

    private async Task<IActionResult> Record(LiquidTransactionRequest request, LiquidStakingOperation operation, CancellationToken cancellationToken)
    {
        if (!TryGetChain(request.ChainKey, out var chain) || !TryNormalizeWallet(chain, request.WalletIdentifier, out var wallet)) return BadRequest("A valid enabled chain and wallet are required.");
        if (!string.Equals(selectedChain.SelectedChainKey, chain.Key, StringComparison.Ordinal)) return BadRequest("The transaction chain does not match the selected chain.");
        if (!WalletMatchesSession(chain, wallet)) return Unauthorized("The authenticated wallet does not match this transaction.");
        var transactionId = request.TransactionId.Trim();
        if ((chain.Family == ChainFamily.Evm && !WalletAddressRules.TryNormalizeTransactionHash(transactionId, out transactionId)) ||
            (chain.Family == ChainFamily.Solana && (!SolanaBase58.TryDecode(transactionId, out var signatureBytes) || signatureBytes.Length != 64))) return BadRequest("A valid transaction identifier is required.");
        var verification = await gateway.VerifyAsync(chain.Key, wallet, transactionId, operation, request.ExpectedAmount, cancellationToken);
        if (verification.Status == TransactionVerificationStatus.PendingConfirmations) return StatusCode(StatusCodes.Status202Accepted, new { status = "pending_confirmations", confirmations = 0, requiredConfirmations = chain.MinimumConfirmations });
        if (!verification.Verified) return BadRequest(verification.Error ?? "Liquid-staking transaction could not be verified.");

        var existing = await dbContext.StakingLedgerEntries.AsNoTracking().SingleOrDefaultAsync(item =>
            item.ChainKey == chain.Key && item.TransactionHash == transactionId && item.OperationIndex == verification.OperationIndex, cancellationToken);
        if (existing is not null) return Ok(ToResult(existing));

        var entry = new StakingLedgerEntry
        {
            WalletAddress = wallet,
            ChainKey = chain.Key,
            Family = chain.FamilyName,
            ActionType = operation switch { LiquidStakingOperation.Deposit => "deposit", LiquidStakingOperation.Redeem => "redeem", LiquidStakingOperation.Claim => "claim", _ => "reward_funding" },
            Amount = verification.AssetAmount != 0m ? verification.AssetAmount : verification.RewardAmount,
            AssetAmount = verification.AssetAmount,
            ShareAmount = verification.ShareAmount,
            RewardAmount = verification.RewardAmount,
            RawAssetAmount = ToRaw(verification.AssetAmount, chain.Deployment.CafeDecimals),
            RawShareAmount = ToRaw(verification.ShareAmount, chain.Deployment.StCafeDecimals),
            RawRewardAmount = ToRaw(verification.RewardAmount, chain.Deployment.CoffeeDecimals),
            TransactionHash = transactionId,
            OperationIndex = verification.OperationIndex,
            ChainId = chain.EvmChainId ?? 0,
            NetworkName = chain.DisplayName,
            PaymentTokenContract = chain.Deployment.Cafe,
            StakingPoolContract = chain.Family == ChainFamily.Solana ? chain.Deployment.Program : chain.Deployment.LiquidVault,
            AssetIdentifier = chain.Deployment.Cafe,
            ReceiptIdentifier = chain.Deployment.StCafe,
            RewardIdentifier = chain.Deployment.Coffee,
            VaultOrProgramIdentifier = chain.Family == ChainFamily.Solana ? chain.Deployment.Program : chain.Deployment.LiquidVault,
            BlockOrSlot = verification.BlockNumber,
            Verified = true,
            VerificationState = "verified",
            ExplorerUrl = string.Format(chain.ExplorerTransactionTemplate, transactionId),
            RecordedAtUtc = DateTime.UtcNow,
            OccurredAtUtc = DateTime.UtcNow
        };
        dbContext.StakingLedgerEntries.Add(entry);
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            var concurrent = await dbContext.StakingLedgerEntries.AsNoTracking().SingleOrDefaultAsync(item =>
                item.ChainKey == chain.Key && item.TransactionHash == transactionId && item.OperationIndex == verification.OperationIndex, cancellationToken);
            if (concurrent is not null) return Ok(ToResult(concurrent));
            return Conflict("The verified operation could not be recorded safely.");
        }
        return Ok(ToResult(entry));
    }

    private static object ToResult(StakingLedgerEntry entry) => new { success = true, entry.ChainKey, entry.TransactionHash, entry.OperationIndex, entry.ActionType, entry.ExplorerUrl };
    internal static string ToRaw(decimal value, int decimals) =>
        decimal.Truncate(value * (decimal)System.Numerics.BigInteger.Pow(10, decimals)).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    private bool TryGetChain(string chainKey, out ChainDefinition chain) => registry.TryGet(chainKey, out chain!) && chain.Enabled;
    private static bool TryNormalizeWallet(ChainDefinition chain, string value, out string wallet)
    {
        if (chain.Family == ChainFamily.Evm) return WalletAddressRules.TryNormalizeWallet(value, out wallet);
        wallet = value.Trim();
        return SolanaBase58.IsPublicKey(wallet);
    }
    private bool WalletMatchesSession(ChainDefinition chain, string wallet)
    {
        var sessionWallet = HttpContext.Session.GetString("WalletAddress") ?? User.FindFirst("wallet_address")?.Value ?? string.Empty;
        return chain.Family == ChainFamily.Evm ? AddressUtil.Current.AreAddressesTheSame(wallet, sessionWallet) : string.Equals(wallet, sessionWallet, StringComparison.Ordinal);
    }

    public sealed record LiquidTransactionRequest(string ChainKey, string WalletIdentifier, string TransactionId, decimal? ExpectedAmount = null);
}
