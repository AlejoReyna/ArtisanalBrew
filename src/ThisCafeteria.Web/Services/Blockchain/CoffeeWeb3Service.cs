using System.Numerics;
using Microsoft.Extensions.Options;
using Nethereum.Hex.HexTypes;
using Nethereum.JsonRpc.Client;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Web.Configuration;

namespace ThisCafeteria.Web.Services.Blockchain;

public sealed class CoffeeWeb3Service : ICoffeeWeb3Service
{
    private readonly Web3 _readOnlyWeb3;
    private readonly BnbTestnetOptions _chain;
    private readonly CoffeeCoinOwnerOptions _owner;
    private readonly string _ankrBnbContract;
    private readonly string _coffeeCoinContract;

    public CoffeeWeb3Service(
        IOptions<BnbTestnetOptions> chainOptions,
        IOptions<CoffeeCoinOwnerOptions> ownerOptions)
    {
        _chain = chainOptions.Value;
        _owner = ownerOptions.Value;
        _readOnlyWeb3 = new Web3(_chain.RpcUrl);
        _ankrBnbContract = _chain.AnkrBNBContract;
        _coffeeCoinContract = _chain.CoffeeCoinContract;
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

        var bnbBalanceTask = _readOnlyWeb3.Eth.GetBalance.SendRequestAsync(walletAddress);
        var ankrBalanceTask = GetErc20BalanceAsync(walletAddress, _ankrBnbContract, cancellationToken);
        var coffeeBalanceTask = GetCoffeeCoinBalanceAsync(walletAddress, cancellationToken);

        await Task.WhenAll(bnbBalanceTask, ankrBalanceTask, coffeeBalanceTask).ConfigureAwait(false);

        var bnbWei = await bnbBalanceTask.ConfigureAwait(false);

        return new CoffeeDashboardModel
        {
            WalletAddress = walletAddress,
            BnbBalance = Web3.Convert.FromWei(bnbWei.Value),
            AnkrBnbBalance = await ankrBalanceTask.ConfigureAwait(false),
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
            throw new InvalidOperationException("Blockchain:BNBTestnet:CoffeeCoinContract is not configured.");
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
                "CoffeeCoin mint failed because the configured owner wallet does not have enough tBNB to pay gas.",
                exception);
        }
        catch (Exception exception) when (IsGasFundingFailure(exception))
        {
            throw new InvalidOperationException(
                "CoffeeCoin mint failed because the configured owner wallet does not have enough tBNB to pay gas.",
                exception);
        }
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
}
