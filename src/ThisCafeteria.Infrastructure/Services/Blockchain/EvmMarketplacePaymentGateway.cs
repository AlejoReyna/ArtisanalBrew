using Microsoft.Extensions.Logging;
using System.Numerics;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Util;
using Nethereum.Web3;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.Infrastructure.Services.Blockchain;

public sealed class EvmMarketplacePaymentGateway(
    IChainRegistry registry,
    ILogger<EvmMarketplacePaymentGateway> logger) : IMarketplacePaymentGateway
{
    public async Task<StakingVerificationResult> VerifyNativePaymentAsync(
        string chainKey,
        string transactionHash,
        string expectedFromWallet,
        decimal expectedAmount,
        CancellationToken cancellationToken = default)
    {
        if (!registry.TryGet(chainKey, out var chain) ||
            !chain.Enabled ||
            chain.Family != ChainFamily.Evm ||
            !chain.Capabilities.MarketplacePayment ||
            string.IsNullOrWhiteSpace(chain.Deployment.LegacyPool))
        {
            return StakingVerificationResult.Failed;
        }

        if (!IsTransactionHash(transactionHash) || !IsValidAddress(expectedFromWallet) || expectedAmount <= 0m)
        {
            return StakingVerificationResult.Failed;
        }

        try
        {
            var web3 = new Web3(chain.EffectiveServerRpcUrl);
            var receipt = await web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(transactionHash).ConfigureAwait(false);
            if (receipt is null)
            {
                // Not yet mined - this is the expected state on the first poll or two right after a
                // broadcast, not a failure. The caller retries on Pending and only gives up on a
                // genuine Failed (wrong recipient, reverted), so this must not throw prematurely.
                return StakingVerificationResult.Pending(0, chain.MinimumConfirmations);
            }

            if (receipt.Status?.Value != BigInteger.One || !AddressMatches(receipt.To, chain.Deployment.LegacyPool))
            {
                return StakingVerificationResult.Failed;
            }

            var confirmations = await GetConfirmationsAsync(web3, receipt, cancellationToken).ConfigureAwait(false);
            if (confirmations < chain.MinimumConfirmations)
            {
                return StakingVerificationResult.Pending(confirmations, chain.MinimumConfirmations);
            }

            var transaction = await web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(transactionHash).ConfigureAwait(false);
            var expectedAmountWei = Web3.Convert.ToWei(expectedAmount);
            if (transaction is null ||
                !AddressMatches(transaction.From, expectedFromWallet) ||
                !AddressMatches(transaction.To, chain.Deployment.LegacyPool) ||
                transaction.Value?.Value != expectedAmountWei)
            {
                return StakingVerificationResult.Failed;
            }

            return new StakingVerificationResult(
                TransactionVerificationStatus.Verified,
                expectedAmount,
                confirmations,
                chain.MinimumConfirmations);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Marketplace payment verification failed for chain {ChainKey} and transaction {TransactionHash}", chainKey, transactionHash);
            return StakingVerificationResult.Failed;
        }
    }

    private static bool AddressMatches(string? actual, string expected) => !string.IsNullOrWhiteSpace(actual) && actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    private static bool IsValidAddress(string? address) => !string.IsNullOrWhiteSpace(address) && AddressUtil.Current.IsValidEthereumAddressHexFormat(address);
    private static bool IsTransactionHash(string value) => value.Length == 66 && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && value[2..].All(Uri.IsHexDigit);
    private static async Task<int> GetConfirmationsAsync(Web3 web3, TransactionReceipt receipt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (receipt.BlockNumber is null) return 0;
        var current = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync().ConfigureAwait(false);
        return ConfirmationCalculator.Calculate(current.Value, receipt.BlockNumber.Value);
    }
}
