using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Web.Configuration;

namespace ThisCafeteria.Web.Services.Blockchain;

public sealed class CoffeeWeb3Service : ICoffeeWeb3Service
{
    private readonly Web3 _readOnlyWeb3;
    private readonly BlockchainNetworkOptions _chain;
    private readonly CoffeeCoinOwnerOptions _owner;
    private readonly string _paymentTokenContract;
    private readonly string _coffeeCoinContract;
    private readonly string _stakingPoolContract;

    public CoffeeWeb3Service(
        IOptions<BlockchainNetworkOptions> chainOptions,
        IOptions<CoffeeCoinOwnerOptions> ownerOptions)
    {
        _chain = chainOptions.Value;
        _owner = ownerOptions.Value;
        _readOnlyWeb3 = new Web3(_chain.RpcUrl);
        _paymentTokenContract = _chain.EffectivePaymentTokenContract;
        _coffeeCoinContract = _chain.CoffeeCoinContract;
        _stakingPoolContract = _chain.StakingPoolContract;
    }

    public bool IsMintingConfigured => _owner.IsConfigured;

    public Task<decimal> GetCoffeeCoinBalanceAsync(
        string walletAddress,
        CancellationToken cancellationToken = default) =>
        GetErc20BalanceAsync(walletAddress, _coffeeCoinContract, cancellationToken);

    public async Task<decimal> GetTotalCoffeeSupplyAsync(CancellationToken cancellationToken = default)
    {
        if (!IsValidContract(_coffeeCoinContract))
        {
            return 0m;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var contract = _readOnlyWeb3.Eth.GetContract(ContractAbis.Erc20TotalSupply, _coffeeCoinContract);
        var supplyWei = await contract
            .GetFunction("totalSupply")
            .CallAsync<BigInteger>()
            .ConfigureAwait(false);

        return Web3.Convert.FromWei(supplyWei);
    }

    public async Task<CoffeeDashboardModel> GetDashboardDataAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            return new CoffeeDashboardModel();
        }

        var nativeBalanceTask = _readOnlyWeb3.Eth.GetBalance.SendRequestAsync(walletAddress);
        var paymentTokenBalanceTask = GetErc20BalanceAsync(walletAddress, _paymentTokenContract, cancellationToken);
        var stakedBalanceTask = GetStakedPaymentTokenBalanceAsync(walletAddress, cancellationToken);
        var pendingRewardsTask = GetPendingStakingRewardsAsync(walletAddress, cancellationToken);
        var coffeeBalanceTask = GetCoffeeCoinBalanceAsync(walletAddress, cancellationToken);

        await Task.WhenAll(
                nativeBalanceTask,
                paymentTokenBalanceTask,
                stakedBalanceTask,
                pendingRewardsTask,
                coffeeBalanceTask)
            .ConfigureAwait(false);

        var nativeWei = await nativeBalanceTask.ConfigureAwait(false);

        return new CoffeeDashboardModel
        {
            WalletAddress = walletAddress,
            NativeBalance = Web3.Convert.FromWei(nativeWei.Value),
            PaymentTokenBalance = await paymentTokenBalanceTask.ConfigureAwait(false),
            StakedPaymentTokenBalance = await stakedBalanceTask.ConfigureAwait(false),
            PendingStakingRewards = await pendingRewardsTask.ConfigureAwait(false),
            CoffeeCoinBalance = await coffeeBalanceTask.ConfigureAwait(false),
            CurrentApr = _chain.StakingAprPercent
        };
    }

    public async Task<string> MintCoffeeCoinAsync(
        string toAddress,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        if (!_owner.IsConfigured)
        {
            throw new InvalidOperationException(
                "CoffeeCoin owner private key is not configured. " +
                "Set CoffeeCoinOwner:PrivateKey via User Secrets or environment variable CoffeeCoinOwner__PrivateKey.");
        }

        if (!IsValidContract(_coffeeCoinContract))
        {
            throw new InvalidOperationException("Blockchain:Network:CoffeeCoinContract is not configured.");
        }

        if (!IsValidAddress(toAddress))
        {
            throw new ArgumentException("A valid recipient wallet address is required.", nameof(toAddress));
        }

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Mint amount must be greater than zero.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var privateKey = NormalizePrivateKey(_owner.PrivateKey!);
        var account = new Account(privateKey, _chain.ChainId);
        var signingWeb3 = new Web3(account, _chain.RpcUrl);

        var contract = signingWeb3.Eth.GetContract(ContractAbis.CoffeeCoinMint, _coffeeCoinContract);
        var mintFunction = contract.GetFunction("mint");
        BigInteger amountWei = Web3.Convert.ToWei(amount);

        try
        {
            var estimatedGas = await mintFunction
                .EstimateGasAsync(account.Address, null, null, toAddress, amountWei)
                .ConfigureAwait(false);

            var receipt = await mintFunction
                .SendTransactionAndWaitForReceiptAsync(
                    account.Address,
                    new HexBigInteger(estimatedGas.Value + 50_000),
                    null,
                    null,
                    toAddress,
                    amountWei)
                .ConfigureAwait(false);

            if (receipt.Failed())
            {
                throw new InvalidOperationException($"CoffeeCoin mint transaction failed. Hash: {receipt.TransactionHash}");
            }

            return receipt.TransactionHash;
        }
        catch (RpcResponseException exception) when (IsGasFundingFailure(exception))
        {
            throw new InvalidOperationException(
                $"CoffeeCoin mint failed because the configured owner wallet does not have enough {_chain.CurrencySymbol} on {_chain.NetworkName} to pay gas.",
                exception);
        }
        catch (Exception exception) when (IsGasFundingFailure(exception))
        {
            throw new InvalidOperationException(
                $"CoffeeCoin mint failed because the configured owner wallet does not have enough {_chain.CurrencySymbol} on {_chain.NetworkName} to pay gas.",
                exception);
        }
    }

    public async Task<bool> VerifyPaymentTransactionAsync(
        string txHash,
        string expectedCustomer,
        decimal expectedAmount,
        CancellationToken cancellationToken = default)
    {
        if (!IsTransactionHash(txHash) ||
            !IsValidAddress(expectedCustomer) ||
            expectedAmount <= 0m ||
            !IsValidContract(_paymentTokenContract) ||
            !IsValidContract(_chain.MarketplaceWallet))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var receipt = await _readOnlyWeb3.Eth.Transactions
            .GetTransactionReceipt
            .SendRequestAsync(txHash)
            .ConfigureAwait(false);

        if (receipt is null ||
            receipt.Status?.Value != BigInteger.One ||
            !AddressMatches(receipt.To, _paymentTokenContract))
        {
            return false;
        }

        var transaction = await _readOnlyWeb3.Eth.Transactions
            .GetTransactionByHash
            .SendRequestAsync(txHash)
            .ConfigureAwait(false);

        if (transaction is null ||
            !AddressMatches(transaction.From, expectedCustomer) ||
            !AddressMatches(transaction.To, _paymentTokenContract))
        {
            return false;
        }

        var expectedAmountWei = Web3.Convert.ToWei(expectedAmount);
        if (!TryDecodeErc20Transfer(transaction.Input, out var recipient, out var transferredAmountWei) ||
            !AddressMatches(recipient, _chain.MarketplaceWallet) ||
            transferredAmountWei != expectedAmountWei)
        {
            return false;
        }

        var transferEvents = receipt.DecodeAllEvents<TransferEventDTO>();
        return transferEvents.Any(transfer =>
            AddressMatches(transfer.Log.Address, _paymentTokenContract) &&
            AddressMatches(transfer.Event.From, expectedCustomer) &&
            AddressMatches(transfer.Event.To, _chain.MarketplaceWallet) &&
            transfer.Event.Value == expectedAmountWei);
    }

    public async Task<decimal> GetStakedPaymentTokenBalanceAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidAddress(walletAddress) || !IsValidContract(_stakingPoolContract))
        {
            return 0m;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var contract = _readOnlyWeb3.Eth.GetContract(ContractAbis.StakingPool, _stakingPoolContract);
        var balanceWei = await contract
            .GetFunction("balanceOf")
            .CallAsync<BigInteger>(walletAddress)
            .ConfigureAwait(false);

        return Web3.Convert.FromWei(balanceWei);
    }

    public async Task<decimal> GetPendingStakingRewardsAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidAddress(walletAddress) || !IsValidContract(_stakingPoolContract))
        {
            return 0m;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var contract = _readOnlyWeb3.Eth.GetContract(ContractAbis.StakingPool, _stakingPoolContract);
            var rewardsWei = await contract
                .GetFunction("earned")
                .CallAsync<BigInteger>(walletAddress)
                .ConfigureAwait(false);

            return Web3.Convert.FromWei(rewardsWei);
        }
        catch (Exception)
        {
            return 0m;
        }
    }

    public async Task<bool> VerifyStakingTransactionAsync(
        string txHash,
        string expectedWallet,
        decimal expectedAmount,
        StakingTransactionType transactionType,
        CancellationToken cancellationToken = default)
    {
        if (!IsTransactionHash(txHash) ||
            !IsValidAddress(expectedWallet) ||
            expectedAmount <= 0m ||
            !IsValidContract(_paymentTokenContract) ||
            !IsValidContract(_stakingPoolContract))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var receipt = await _readOnlyWeb3.Eth.Transactions
            .GetTransactionReceipt
            .SendRequestAsync(txHash)
            .ConfigureAwait(false);

        if (receipt is null ||
            receipt.Status?.Value != BigInteger.One ||
            !AddressMatches(receipt.To, _stakingPoolContract))
        {
            return false;
        }

        var transaction = await _readOnlyWeb3.Eth.Transactions
            .GetTransactionByHash
            .SendRequestAsync(txHash)
            .ConfigureAwait(false);

        if (transaction is null ||
            !AddressMatches(transaction.From, expectedWallet) ||
            !AddressMatches(transaction.To, _stakingPoolContract))
        {
            return false;
        }

        var expectedAmountWei = Web3.Convert.ToWei(expectedAmount);
        if (!TryDecodeStakingAmount(transaction.Input, transactionType, out var callAmountWei) ||
            callAmountWei != expectedAmountWei)
        {
            return false;
        }

        var transferEvents = receipt.DecodeAllEvents<TransferEventDTO>();
        return transactionType switch
        {
            StakingTransactionType.Stake => transferEvents.Any(transfer =>
                AddressMatches(transfer.Log.Address, _paymentTokenContract) &&
                AddressMatches(transfer.Event.From, expectedWallet) &&
                AddressMatches(transfer.Event.To, _stakingPoolContract) &&
                transfer.Event.Value == expectedAmountWei),
            StakingTransactionType.Unstake => transferEvents.Any(transfer =>
                AddressMatches(transfer.Log.Address, _paymentTokenContract) &&
                AddressMatches(transfer.Event.From, _stakingPoolContract) &&
                AddressMatches(transfer.Event.To, expectedWallet) &&
                transfer.Event.Value == expectedAmountWei),
            _ => false
        };
    }

    private async Task<decimal> GetErc20BalanceAsync(
        string walletAddress,
        string contractAddress,
        CancellationToken cancellationToken)
    {
        if (!IsValidContract(contractAddress))
        {
            return 0m;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var contract = _readOnlyWeb3.Eth.GetContract(ContractAbis.Erc20BalanceOf, contractAddress);
        var balanceWei = await contract
            .GetFunction("balanceOf")
            .CallAsync<BigInteger>(walletAddress)
            .ConfigureAwait(false);

        return Web3.Convert.FromWei(balanceWei);
    }

    private static bool IsValidContract(string? address) =>
        IsValidAddress(address) &&
        !address!.Equals("0x0000000000000000000000000000000000000000", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidAddress(string? address) =>
        !string.IsNullOrWhiteSpace(address) &&
        AddressUtil.Current.IsValidEthereumAddressHexFormat(address);

    private static bool IsTransactionHash(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length == 66 &&
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
        value[2..].All(Uri.IsHexDigit);

    private static bool AddressMatches(string? actual, string expected) =>
        !string.IsNullOrWhiteSpace(actual) &&
        actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool TryDecodeErc20Transfer(
        string? input,
        out string recipient,
        out BigInteger amount)
    {
        const string transferSelector = "0xa9059cbb";

        recipient = string.Empty;
        amount = BigInteger.Zero;

        if (string.IsNullOrWhiteSpace(input) ||
            !input.StartsWith(transferSelector, StringComparison.OrdinalIgnoreCase) ||
            input.Length < 138)
        {
            return false;
        }

        recipient = $"0x{input.Substring(10 + 24, 40)}";
        amount = BigInteger.Parse($"0{input.Substring(10 + 64, 64)}", System.Globalization.NumberStyles.HexNumber);
        return IsValidAddress(recipient);
    }

    private static bool TryDecodeStakingAmount(
        string? input,
        StakingTransactionType transactionType,
        out BigInteger amount)
    {
        amount = BigInteger.Zero;

        if (string.IsNullOrWhiteSpace(input) || input.Length < 74)
        {
            return false;
        }

        var stakeSelector = FunctionSelector("stake(uint256)");
        var unstakeSelector = FunctionSelector("unstake(uint256)");
        var withdrawSelector = FunctionSelector("withdraw(uint256)");
        var selectorMatches = transactionType switch
        {
            StakingTransactionType.Stake => input.StartsWith(stakeSelector, StringComparison.OrdinalIgnoreCase),
            StakingTransactionType.Unstake =>
                input.StartsWith(unstakeSelector, StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith(withdrawSelector, StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        if (!selectorMatches)
        {
            return false;
        }

        amount = BigInteger.Parse($"0{input.Substring(10, 64)}", System.Globalization.NumberStyles.HexNumber);
        return true;
    }

    private static string FunctionSelector(string signature)
    {
        var hash = Sha3Keccack.Current.CalculateHash(signature);
        return $"0x{hash[..8]}";
    }

    private static bool IsGasFundingFailure(Exception exception)
    {
        var message = exception.Message;
        return message.Contains("insufficient funds", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("insufficient balance", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("gas required exceeds allowance", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePrivateKey(string privateKey)
    {
        var trimmed = privateKey.Trim();
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? trimmed[2..]
            : trimmed;
    }

    [Event("Transfer")]
    private sealed class TransferEventDTO : IEventDTO
    {
        [Parameter("address", "from", 1, true)]
        public string From { get; set; } = string.Empty;

        [Parameter("address", "to", 2, true)]
        public string To { get; set; } = string.Empty;

        [Parameter("uint256", "value", 3, false)]
        public BigInteger Value { get; set; }
    }
}
