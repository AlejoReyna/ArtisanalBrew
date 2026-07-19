using System.Numerics;
using Nethereum.Contracts;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.Web.Services.Blockchain;

public sealed class EvmLiquidStakingGateway(
    IChainRegistry registry,
    ILogger<EvmLiquidStakingGateway> logger) : ILiquidStakingGateway
{
    public async Task<LiquidStakingDashboard> GetDashboardAsync(string chainKey, string walletIdentifier, CancellationToken cancellationToken = default)
    {
        if (!registry.TryGet(chainKey, out var chain) || !chain.Enabled)
            return Unavailable(chainKey, "The selected chain is unknown or disabled.", walletIdentifier);
        if (chain.Family != ChainFamily.Evm)
            return Unavailable(chain.Key, "This gateway only serves EVM chains.", walletIdentifier);
        if (!chain.Capabilities.LiquidStaking || string.IsNullOrWhiteSpace(chain.Deployment.LiquidVault))
            return Unavailable(chain.Key, "Liquid staking is not deployed on this chain yet.", walletIdentifier);
        if (!AddressRules(walletIdentifier) || !AddressRules(chain.Deployment.Cafe) || !AddressRules(chain.Deployment.Coffee) || !AddressRules(chain.Deployment.StCafe))
            return Unavailable(chain.Key, "The selected chain has an incomplete liquid-staking manifest.", walletIdentifier);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var web3 = new Web3(chain.EffectiveServerRpcUrl);
            var vault = web3.Eth.GetContract(ContractAbis.LiquidVault, chain.Deployment.LiquidVault);
            var cafe = web3.Eth.GetContract(ContractAbis.Erc20BalanceOf, chain.Deployment.Cafe);
            var shares = web3.Eth.GetContract(ContractAbis.Erc20BalanceOf, chain.Deployment.StCafe);
            var coffee = web3.Eth.GetContract(ContractAbis.Erc20BalanceOf, chain.Deployment.Coffee);
            var cafeBalance = await cafe.GetFunction("balanceOf").CallAsync<BigInteger>(walletIdentifier).ConfigureAwait(false);
            var shareBalance = await shares.GetFunction("balanceOf").CallAsync<BigInteger>(walletIdentifier).ConfigureAwait(false);
            var coffeeBalance = await coffee.GetFunction("balanceOf").CallAsync<BigInteger>(walletIdentifier).ConfigureAwait(false);
            var redeemable = await vault.GetFunction("previewRedeem").CallAsync<BigInteger>(shareBalance).ConfigureAwait(false);
            var preview = await vault.GetFunction("previewDeposit").CallAsync<BigInteger>(BigInteger.Pow(10, 18)).ConfigureAwait(false);
            var earned = await vault.GetFunction("earned").CallAsync<BigInteger>(walletIdentifier).ConfigureAwait(false);
            var native = await web3.Eth.GetBalance.SendRequestAsync(walletIdentifier).ConfigureAwait(false);
            var assets = await vault.GetFunction("totalAssets").CallAsync<BigInteger>().ConfigureAwait(false);
            var supply = await vault.GetFunction("totalSupply").CallAsync<BigInteger>().ConfigureAwait(false);

            return new LiquidStakingDashboard
            {
                ChainKey = chain.Key,
                Family = chain.FamilyName,
                WalletIdentifier = walletIdentifier,
                IsConfigured = true,
                CafeBalance = Web3.Convert.FromWei(cafeBalance),
                StCafeBalance = Web3.Convert.FromWei(shareBalance),
                RedeemableCafe = Web3.Convert.FromWei(redeemable),
                ExchangeRate = supply == 0 ? 1m : Web3.Convert.FromWei(assets) / Web3.Convert.FromWei(supply),
                PendingCoffee = Web3.Convert.FromWei(earned),
                CoffeeBalance = Web3.Convert.FromWei(coffeeBalance),
                DepositPreviewShares = Web3.Convert.FromWei(preview),
                NativeGasBalance = Web3.Convert.FromWei(native.Value),
                VaultIdentifier = chain.Deployment.LiquidVault,
                AssetIdentifier = chain.Deployment.Cafe,
                ReceiptIdentifier = chain.Deployment.StCafe,
                RewardIdentifier = chain.Deployment.Coffee
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Liquid dashboard read failed for chain {ChainKey}", chainKey);
            return Unavailable(chain.Key, "The selected chain RPC is unavailable. Try again shortly.", walletIdentifier);
        }
    }

    public async Task<LiquidTransactionVerificationResult> VerifyAsync(string chainKey, string walletIdentifier, string transactionId, LiquidStakingOperation operation, decimal? expectedAmount, CancellationToken cancellationToken = default)
    {
        if (!registry.TryGet(chainKey, out var chain) || chain.Family != ChainFamily.Evm || !chain.Capabilities.LiquidStaking || string.IsNullOrWhiteSpace(chain.Deployment.LiquidVault)) return LiquidTransactionVerificationResult.Failed("Liquid staking is not configured on the selected chain.");
        if (!AddressRules(walletIdentifier) || !IsTransactionHash(transactionId)) return LiquidTransactionVerificationResult.Failed("Invalid wallet or transaction identifier.");
        try
        {
            var web3 = new Web3(chain.EffectiveServerRpcUrl);
            var receipt = await web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(transactionId).ConfigureAwait(false);
            var transaction = await web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(transactionId).ConfigureAwait(false);
            if (receipt is null || transaction is null || receipt.Status?.Value != BigInteger.One || !AddressMatches(receipt.To, chain.Deployment.LiquidVault) || !AddressMatches(transaction.From, walletIdentifier)) return LiquidTransactionVerificationResult.Failed("Receipt, signer, or vault target did not match the trusted registry.");
            var selector = operation switch { LiquidStakingOperation.Deposit => StakingCalldataDecoder.FunctionSelector("deposit(uint256,address)"), LiquidStakingOperation.Redeem => StakingCalldataDecoder.FunctionSelector("redeem(uint256,address,address)"), LiquidStakingOperation.Claim => StakingCalldataDecoder.FunctionSelector("claimRewards()"), _ => string.Empty };
            if (!transaction.Input.StartsWith(selector, StringComparison.OrdinalIgnoreCase)) return LiquidTransactionVerificationResult.Failed("Transaction function did not match the requested operation.");
            var confirmations = await GetConfirmationsAsync(web3, receipt, cancellationToken).ConfigureAwait(false);
            if (confirmations < chain.MinimumConfirmations) return new LiquidTransactionVerificationResult(false, TransactionVerificationStatus.PendingConfirmations, BlockNumber: (long)(receipt.BlockNumber?.Value ?? 0), Error: "Waiting for configured confirmations.");
            if (operation == LiquidStakingOperation.Deposit)
            {
                if (!TryDecodeUint(transaction.Input, 0, out var assets) || !TryDecodeAddress(transaction.Input, 1, out var receiver) || !AddressMatches(receiver, walletIdentifier)) return LiquidTransactionVerificationResult.Failed("Deposit calldata did not match the authenticated receiver.");
                if (expectedAmount is not null && assets != Web3.Convert.ToWei(expectedAmount.Value)) return LiquidTransactionVerificationResult.Failed("Deposit amount did not match the requested amount.");
                var eventLog = receipt.DecodeAllEvents<DepositEventDTO>().FirstOrDefault(item => AddressMatches(item.Log.Address, chain.Deployment.LiquidVault) && AddressMatches(item.Event.Receiver, walletIdentifier) && item.Event.Assets == assets);
                return eventLog is null ? LiquidTransactionVerificationResult.Failed("The vault deposit event was not observed.") : new LiquidTransactionVerificationResult(true, TransactionVerificationStatus.Verified, AssetAmount: Web3.Convert.FromWei(eventLog.Event.Assets), ShareAmount: Web3.Convert.FromWei(eventLog.Event.Shares), BlockNumber: (long)(receipt.BlockNumber?.Value ?? 0), OperationIndex: LogIndex(eventLog.Log));
            }
            if (operation == LiquidStakingOperation.Redeem)
            {
                if (!TryDecodeUint(transaction.Input, 0, out var shares) || !TryDecodeAddress(transaction.Input, 1, out var receiver) || !TryDecodeAddress(transaction.Input, 2, out var owner) || !AddressMatches(receiver, walletIdentifier) || !AddressMatches(owner, walletIdentifier)) return LiquidTransactionVerificationResult.Failed("Redeem calldata did not match the authenticated owner and receiver.");
                if (expectedAmount is not null && shares != Web3.Convert.ToWei(expectedAmount.Value)) return LiquidTransactionVerificationResult.Failed("Redeem share amount did not match the requested amount.");
                var eventLog = receipt.DecodeAllEvents<WithdrawEventDTO>().FirstOrDefault(item => AddressMatches(item.Log.Address, chain.Deployment.LiquidVault) && AddressMatches(item.Event.Receiver, walletIdentifier) && item.Event.Shares == shares);
                return eventLog is null ? LiquidTransactionVerificationResult.Failed("The vault withdrawal event was not observed.") : new LiquidTransactionVerificationResult(true, TransactionVerificationStatus.Verified, AssetAmount: Web3.Convert.FromWei(eventLog.Event.Assets), ShareAmount: Web3.Convert.FromWei(eventLog.Event.Shares), BlockNumber: (long)(receipt.BlockNumber?.Value ?? 0), OperationIndex: LogIndex(eventLog.Log));
            }
            var rewardTransfer = receipt.DecodeAllEvents<TransferEventDTO>().FirstOrDefault(item => AddressMatches(item.Log.Address, chain.Deployment.Coffee) && AddressMatches(item.Event.From, chain.Deployment.LiquidVault) && AddressMatches(item.Event.To, walletIdentifier));
            return rewardTransfer is null ? LiquidTransactionVerificationResult.Failed("No COFFEE reward transfer was observed.") : new LiquidTransactionVerificationResult(true, TransactionVerificationStatus.Verified, RewardAmount: Web3.Convert.FromWei(rewardTransfer.Event.Value), BlockNumber: (long)(receipt.BlockNumber?.Value ?? 0), OperationIndex: LogIndex(rewardTransfer.Log));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Liquid transaction verification failed for chain {ChainKey} and transaction {TransactionId}", chainKey, transactionId);
            return LiquidTransactionVerificationResult.Failed("The transaction could not be verified.");
        }
    }

    private static LiquidStakingDashboard Unavailable(string chainKey, string reason, string wallet) => new() { ChainKey = chainKey, Family = "Evm", WalletIdentifier = wallet, UnavailableReason = reason };
    private static bool AddressRules(string? address) => !string.IsNullOrWhiteSpace(address) && AddressUtil.Current.IsValidEthereumAddressHexFormat(address);
    private static bool AddressMatches(string? actual, string expected) => !string.IsNullOrWhiteSpace(actual) && actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    private static bool IsTransactionHash(string value) => value.Length == 66 && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && value[2..].All(Uri.IsHexDigit);
    private static int LogIndex(Nethereum.RPC.Eth.DTOs.FilterLog log) => checked((int)(log.LogIndex?.Value ?? 0));
    private static async Task<int> GetConfirmationsAsync(Web3 web3, TransactionReceipt receipt, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); if (receipt.BlockNumber is null) return 0; var current = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync().ConfigureAwait(false); return ConfirmationCalculator.Calculate(current.Value, receipt.BlockNumber.Value); }

    private static bool TryDecodeUint(string input, int index, out BigInteger value)
    {
        value = BigInteger.Zero;
        var start = 10 + index * 64;
        if (input.Length < start + 64) return false;
        value = BigInteger.Parse($"0{input.Substring(start, 64)}", System.Globalization.NumberStyles.HexNumber);
        return true;
    }

    private static bool TryDecodeAddress(string input, int index, out string address)
    {
        address = string.Empty;
        var start = 10 + index * 64;
        if (input.Length < start + 64) return false;
        address = $"0x{input.Substring(start + 24, 40)}";
        return AddressUtil.Current.IsValidEthereumAddressHexFormat(address);
    }

    [Event("Deposit")]
    private sealed class DepositEventDTO : IEventDTO
    {
        [Parameter("address", "caller", 1, true)] public string Caller { get; set; } = string.Empty;
        [Parameter("address", "receiver", 2, true)] public string Receiver { get; set; } = string.Empty;
        [Parameter("uint256", "assets", 3, false)] public BigInteger Assets { get; set; }
        [Parameter("uint256", "shares", 4, false)] public BigInteger Shares { get; set; }
    }

    [Event("Withdraw")]
    private sealed class WithdrawEventDTO : IEventDTO
    {
        [Parameter("address", "caller", 1, true)] public string Caller { get; set; } = string.Empty;
        [Parameter("address", "receiver", 2, true)] public string Receiver { get; set; } = string.Empty;
        [Parameter("address", "owner", 3, true)] public string Owner { get; set; } = string.Empty;
        [Parameter("uint256", "assets", 4, false)] public BigInteger Assets { get; set; }
        [Parameter("uint256", "shares", 5, false)] public BigInteger Shares { get; set; }
    }

    [Event("Transfer")]
    private sealed class TransferEventDTO : IEventDTO
    {
        [Parameter("address", "from", 1, true)] public string From { get; set; } = string.Empty;
        [Parameter("address", "to", 2, true)] public string To { get; set; } = string.Empty;
        [Parameter("uint256", "value", 3, false)] public BigInteger Value { get; set; }
    }
}
