using System.Numerics;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Infrastructure.Services.Reconciliation;

public interface ICrossChainIntentProvider
{
    Task<long> GetLatestBlockNumberAsync(ChainDefinition chain, CancellationToken cancellationToken);

    /// <summary>
    /// Scans for IntentSubmitted events in the given block range and returns the FULL intent for
    /// each — decoded from the submitting transaction's own calldata, since the event alone
    /// (orderId, user, destinationChainId, amountIn) doesn't carry enough to call fillIntent.
    /// </summary>
    Task<List<SolverIntent>> GetSubmittedIntentsAsync(ChainDefinition chain, string resolverAddress, long fromBlock, long toBlock, CancellationToken cancellationToken);
}

public sealed class CrossChainIntentProvider : ICrossChainIntentProvider
{
    // keccak256("submitIntent((address,address,uint256,uint256,address,address,uint256,uint256,uint256,address))")[..4]
    private const string SubmitIntentSelector = "0x3e2ab967";

    [Event("IntentSubmitted")]
    private sealed class IntentSubmittedEventDto : IEventDTO
    {
        [Parameter("bytes32", "orderId", 1, true)] public byte[] OrderId { get; set; } = [];
        [Parameter("address", "user", 2, true)] public string User { get; set; } = string.Empty;
        [Parameter("uint256", "destinationChainId", 3, false)] public BigInteger DestinationChainId { get; set; }
        [Parameter("uint256", "amountIn", 4, false)] public BigInteger AmountIn { get; set; }
    }

    [Struct("IntentOrder")]
    private sealed class IntentOrderDto
    {
        [Parameter("address", "user", 1)] public string User { get; set; } = string.Empty;
        [Parameter("address", "sourceToken", 2)] public string SourceToken { get; set; } = string.Empty;
        [Parameter("uint256", "amountIn", 3)] public BigInteger AmountIn { get; set; }
        [Parameter("uint256", "destinationChainId", 4)] public BigInteger DestinationChainId { get; set; }
        [Parameter("address", "destinationToken", 5)] public string DestinationToken { get; set; } = string.Empty;
        [Parameter("address", "destinationReceiver", 6)] public string DestinationReceiver { get; set; } = string.Empty;
        [Parameter("uint256", "minAmountOut", 7)] public BigInteger MinAmountOut { get; set; }
        [Parameter("uint256", "deadline", 8)] public BigInteger Deadline { get; set; }
        [Parameter("uint256", "nonce", 9)] public BigInteger Nonce { get; set; }
        [Parameter("address", "allowedSolver", 10)] public string AllowedSolver { get; set; } = string.Empty;
    }

    [Function("submitIntent")]
    private sealed class SubmitIntentFunction : FunctionMessage
    {
        [Parameter("tuple", "order", 1)] public IntentOrderDto Order { get; set; } = new();
    }

    public async Task<long> GetLatestBlockNumberAsync(ChainDefinition chain, CancellationToken cancellationToken)
    {
        var web3 = new Web3(chain.EffectiveServerRpcUrl);
        return (long)(await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync().ConfigureAwait(false)).Value;
    }

    public async Task<List<SolverIntent>> GetSubmittedIntentsAsync(ChainDefinition chain, string resolverAddress, long fromBlock, long toBlock, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var web3 = new Web3(chain.EffectiveServerRpcUrl);
        var from = new BlockParameter((ulong)fromBlock);
        var to = new BlockParameter((ulong)toBlock);

        var submitted = web3.Eth.GetEvent<IntentSubmittedEventDto>(resolverAddress);
        var logs = await submitted.GetAllChangesAsync(submitted.CreateFilterInput(from, to)).ConfigureAwait(false);

        var decoder = new FunctionCallDecoder();
        var result = new List<SolverIntent>();

        foreach (var item in logs)
        {
            var txHash = item.Log.TransactionHash;
            if (string.IsNullOrEmpty(txHash)) continue;

            var tx = await web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(txHash).ConfigureAwait(false);
            if (tx is null) continue;

            var decoded = decoder.DecodeFunctionInput<SubmitIntentFunction>(SubmitIntentSelector, tx.Input);

            result.Add(new SolverIntent
            {
                OrderId = "0x" + Convert.ToHexString(item.Event.OrderId).ToLowerInvariant(),
                User = decoded.Order.User,
                SourceToken = decoded.Order.SourceToken,
                AmountIn = decoded.Order.AmountIn,
                DestinationChainId = decoded.Order.DestinationChainId,
                DestinationToken = decoded.Order.DestinationToken,
                DestinationReceiver = decoded.Order.DestinationReceiver,
                MinAmountOut = decoded.Order.MinAmountOut,
                Deadline = decoded.Order.Deadline,
                Nonce = decoded.Order.Nonce,
                AllowedSolver = decoded.Order.AllowedSolver,
                SubmitTransactionHash = txHash.ToLowerInvariant()
            });
        }

        return result;
    }
}
