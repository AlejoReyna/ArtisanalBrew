using Microsoft.AspNetCore.Mvc;
using Nethereum.Util;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Web.Services.Blockchain;

namespace ThisCafeteria.Web.Controllers;

[ApiController]
[Route("staking/api/liquid")]
public sealed class LiquidStakingController(
    ILiquidStakingGateway gateway,
    ILiquidStakingLedgerService ledgerService,
    IChainRegistry registry,
    ISelectedChainAccessor selectedChain) : ControllerBase
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
        if (!TryNormalizeTransactionId(chain, request.TransactionId, out var transactionId)) return BadRequest("A valid transaction identifier is required.");

        var result = await ledgerService.RecordAsync(chain, wallet, transactionId, operation, request.ExpectedAmount, cancellationToken);

        return result.Status switch
        {
            LiquidStakingRecordStatus.Recorded or LiquidStakingRecordStatus.AlreadyRecorded => Ok(ToResult(result.Entry!)),
            LiquidStakingRecordStatus.PendingConfirmations => StatusCode(StatusCodes.Status202Accepted, new { status = "pending_confirmations", confirmations = 0, requiredConfirmations = chain.MinimumConfirmations }),
            LiquidStakingRecordStatus.VerificationFailed => BadRequest(result.Error ?? "Liquid-staking transaction could not be verified."),
            _ => Conflict("The verified operation could not be recorded safely.")
        };
    }

    private static bool TryNormalizeTransactionId(ChainDefinition chain, string value, out string transactionId)
    {
        transactionId = value.Trim();

        if (chain.Family == ChainFamily.Evm)
        {
            return WalletAddressRules.TryNormalizeTransactionHash(transactionId, out transactionId);
        }

        return SolanaBase58.TryDecode(transactionId, out var signatureBytes) && signatureBytes.Length == 64;
    }

    private static object ToResult(StakingLedgerEntry entry) => new { success = true, entry.ChainKey, entry.TransactionHash, entry.OperationIndex, entry.ActionType, entry.ExplorerUrl };

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
