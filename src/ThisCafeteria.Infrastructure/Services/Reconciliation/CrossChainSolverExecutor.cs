using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Infrastructure.Services.Reconciliation;

public interface ICrossChainSolverExecutor
{
    /// <summary>Submits fillIntent on the destination chain using the solver's own key. Returns the fill transaction hash.</summary>
    Task<string> FillIntentAsync(ChainDefinition destinationChain, string resolverAddress, string solverPrivateKey, SolverIntent intent, BigInteger amountOut, CancellationToken cancellationToken);
}

public sealed class EvmCrossChainSolverExecutor : ICrossChainSolverExecutor
{
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

    [Function("fillIntent")]
    private sealed class FillIntentFunction : FunctionMessage
    {
        [Parameter("tuple", "order", 1)] public IntentOrderDto Order { get; set; } = new();
        [Parameter("uint256", "amountOut", 2)] public BigInteger AmountOut { get; set; }
    }

    public async Task<string> FillIntentAsync(ChainDefinition destinationChain, string resolverAddress, string solverPrivateKey, SolverIntent intent, BigInteger amountOut, CancellationToken cancellationToken)
    {
        var account = new Account(solverPrivateKey);
        var web3 = new Web3(account, destinationChain.EffectiveServerRpcUrl);

        var function = new FillIntentFunction
        {
            Order = new IntentOrderDto
            {
                User = intent.User,
                SourceToken = intent.SourceToken,
                AmountIn = intent.AmountIn,
                DestinationChainId = intent.DestinationChainId,
                DestinationToken = intent.DestinationToken,
                DestinationReceiver = intent.DestinationReceiver,
                MinAmountOut = intent.MinAmountOut,
                Deadline = intent.Deadline,
                Nonce = intent.Nonce,
                AllowedSolver = intent.AllowedSolver
            },
            AmountOut = amountOut
        };

        var handler = web3.Eth.GetContractTransactionHandler<FillIntentFunction>();
        var receipt = await handler.SendRequestAndWaitForReceiptAsync(resolverAddress, function, cancellationToken).ConfigureAwait(false);
        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException($"fillIntent transaction {receipt.TransactionHash} did not succeed (status {receipt.Status?.Value}).");
        }

        return receipt.TransactionHash.ToLowerInvariant();
    }
}
