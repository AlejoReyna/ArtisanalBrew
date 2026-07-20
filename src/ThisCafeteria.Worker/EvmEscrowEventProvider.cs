using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Hex.HexConvertors.Extensions;
using ThisCafeteria.Application.Configuration;
using static ThisCafeteria.Worker.AgenticCommerceReconciliationWorker;

namespace ThisCafeteria.Worker;

public interface IEscrowEventProvider
{
    Task<long> GetLatestBlockNumberAsync(ChainDefinition chain, CancellationToken cancellationToken);
    Task<List<EscrowEvent>> DecodeEventsAsync(ChainDefinition chain, string escrowAddress, long fromBlock, long toBlock, CancellationToken cancellationToken);
    Task<List<RegistryEvent>> DecodeRegistryEventsAsync(ChainDefinition chain, string registryAddress, long fromBlock, long toBlock, CancellationToken cancellationToken);
}

public class EvmEscrowEventProvider : IEscrowEventProvider
{
    public async Task<long> GetLatestBlockNumberAsync(ChainDefinition chain, CancellationToken cancellationToken)
    {
        var web3 = new Web3(chain.EffectiveServerRpcUrl);
        return (long)(await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync().ConfigureAwait(false)).Value;
    }

    public async Task<List<EscrowEvent>> DecodeEventsAsync(ChainDefinition chain, string escrowAddress, long fromBlock, long toBlock, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var web3 = new Web3(chain.EffectiveServerRpcUrl);
        var from = new BlockParameter((ulong)fromBlock);
        var to = new BlockParameter((ulong)toBlock);
        var result = new List<EscrowEvent>();

        var created = web3.Eth.GetEvent<JobCreatedEventDTO>(escrowAddress);
        foreach (var item in await created.GetAllChangesAsync(created.CreateFilterInput(from, to)).ConfigureAwait(false))
        {
            result.Add(new EscrowEvent
            {
                Type = EscrowEventType.JobCreated,
                OnChainJobId = (long)item.Event.JobId,
                Client = item.Event.Client,
                Provider = item.Event.Provider,
                Evaluator = item.Event.Evaluator,
                ExpiredAt = item.Event.ExpiredAt,
                TransactionHash = item.Log.TransactionHash?.ToLowerInvariant() ?? string.Empty,
                BlockNumber = (long)(item.Log.BlockNumber?.Value ?? 0),
                LogIndex = checked((int)(item.Log.LogIndex?.Value ?? 0))
            });
        }

        var funded = web3.Eth.GetEvent<JobFundedEventDTO>(escrowAddress);
        foreach (var item in await funded.GetAllChangesAsync(funded.CreateFilterInput(from, to)).ConfigureAwait(false))
        {
            result.Add(new EscrowEvent
            {
                Type = EscrowEventType.JobFunded,
                OnChainJobId = (long)item.Event.JobId,
                Client = item.Event.Client,
                Amount = item.Event.Amount,
                TransactionHash = item.Log.TransactionHash?.ToLowerInvariant() ?? string.Empty,
                BlockNumber = (long)(item.Log.BlockNumber?.Value ?? 0),
                LogIndex = checked((int)(item.Log.LogIndex?.Value ?? 0))
            });
        }

        var submitted = web3.Eth.GetEvent<JobSubmittedEventDTO>(escrowAddress);
        foreach (var item in await submitted.GetAllChangesAsync(submitted.CreateFilterInput(from, to)).ConfigureAwait(false))
        {
            result.Add(new EscrowEvent
            {
                Type = EscrowEventType.JobSubmitted,
                OnChainJobId = (long)item.Event.JobId,
                Provider = item.Event.Provider,
                Deliverable = item.Event.Deliverable?.ToHex(true) ?? string.Empty,
                TransactionHash = item.Log.TransactionHash?.ToLowerInvariant() ?? string.Empty,
                BlockNumber = (long)(item.Log.BlockNumber?.Value ?? 0),
                LogIndex = checked((int)(item.Log.LogIndex?.Value ?? 0))
            });
        }

        var completed = web3.Eth.GetEvent<JobCompletedEventDTO>(escrowAddress);
        foreach (var item in await completed.GetAllChangesAsync(completed.CreateFilterInput(from, to)).ConfigureAwait(false))
        {
            result.Add(new EscrowEvent
            {
                Type = EscrowEventType.JobCompleted,
                OnChainJobId = (long)item.Event.JobId,
                Evaluator = item.Event.Evaluator,
                Reason = item.Event.Reason?.ToHex(true) ?? string.Empty,
                TransactionHash = item.Log.TransactionHash?.ToLowerInvariant() ?? string.Empty,
                BlockNumber = (long)(item.Log.BlockNumber?.Value ?? 0),
                LogIndex = checked((int)(item.Log.LogIndex?.Value ?? 0))
            });
        }

        var rejected = web3.Eth.GetEvent<JobRejectedEventDTO>(escrowAddress);
        foreach (var item in await rejected.GetAllChangesAsync(rejected.CreateFilterInput(from, to)).ConfigureAwait(false))
        {
            result.Add(new EscrowEvent
            {
                Type = EscrowEventType.JobRejected,
                OnChainJobId = (long)item.Event.JobId,
                Reason = item.Event.Reason?.ToHex(true) ?? string.Empty,
                TransactionHash = item.Log.TransactionHash?.ToLowerInvariant() ?? string.Empty,
                BlockNumber = (long)(item.Log.BlockNumber?.Value ?? 0),
                LogIndex = checked((int)(item.Log.LogIndex?.Value ?? 0))
            });
        }

        var expired = web3.Eth.GetEvent<JobExpiredEventDTO>(escrowAddress);
        foreach (var item in await expired.GetAllChangesAsync(expired.CreateFilterInput(from, to)).ConfigureAwait(false))
        {
            result.Add(new EscrowEvent
            {
                Type = EscrowEventType.JobExpired,
                OnChainJobId = (long)item.Event.JobId,
                TransactionHash = item.Log.TransactionHash?.ToLowerInvariant() ?? string.Empty,
                BlockNumber = (long)(item.Log.BlockNumber?.Value ?? 0),
                LogIndex = checked((int)(item.Log.LogIndex?.Value ?? 0))
            });
        }

        var providerSet = web3.Eth.GetEvent<ProviderSetEventDTO>(escrowAddress);
        foreach (var item in await providerSet.GetAllChangesAsync(providerSet.CreateFilterInput(from, to)).ConfigureAwait(false))
        {
            result.Add(new EscrowEvent
            {
                Type = EscrowEventType.ProviderSet,
                OnChainJobId = (long)item.Event.JobId,
                Provider = item.Event.Provider,
                TransactionHash = item.Log.TransactionHash?.ToLowerInvariant() ?? string.Empty,
                BlockNumber = (long)(item.Log.BlockNumber?.Value ?? 0),
                LogIndex = checked((int)(item.Log.LogIndex?.Value ?? 0))
            });
        }

        var budgetSet = web3.Eth.GetEvent<BudgetSetEventDTO>(escrowAddress);
        foreach (var item in await budgetSet.GetAllChangesAsync(budgetSet.CreateFilterInput(from, to)).ConfigureAwait(false))
        {
            result.Add(new EscrowEvent
            {
                Type = EscrowEventType.BudgetSet,
                OnChainJobId = (long)item.Event.JobId,
                Amount = item.Event.Amount,
                TransactionHash = item.Log.TransactionHash?.ToLowerInvariant() ?? string.Empty,
                BlockNumber = (long)(item.Log.BlockNumber?.Value ?? 0),
                LogIndex = checked((int)(item.Log.LogIndex?.Value ?? 0))
            });
        }

        var paymentReleased = web3.Eth.GetEvent<PaymentReleasedEventDTO>(escrowAddress);
        foreach (var item in await paymentReleased.GetAllChangesAsync(paymentReleased.CreateFilterInput(from, to)).ConfigureAwait(false))
        {
            result.Add(new EscrowEvent
            {
                Type = EscrowEventType.PaymentReleased,
                OnChainJobId = (long)item.Event.JobId,
                Provider = item.Event.Provider,
                Amount = item.Event.Amount,
                TransactionHash = item.Log.TransactionHash?.ToLowerInvariant() ?? string.Empty,
                BlockNumber = (long)(item.Log.BlockNumber?.Value ?? 0),
                LogIndex = checked((int)(item.Log.LogIndex?.Value ?? 0))
            });
        }

        var refunded = web3.Eth.GetEvent<RefundedEventDTO>(escrowAddress);
        foreach (var item in await refunded.GetAllChangesAsync(refunded.CreateFilterInput(from, to)).ConfigureAwait(false))
        {
            result.Add(new EscrowEvent
            {
                Type = EscrowEventType.Refunded,
                OnChainJobId = (long)item.Event.JobId,
                Client = item.Event.Client,
                Amount = item.Event.Amount,
                TransactionHash = item.Log.TransactionHash?.ToLowerInvariant() ?? string.Empty,
                BlockNumber = (long)(item.Log.BlockNumber?.Value ?? 0),
                LogIndex = checked((int)(item.Log.LogIndex?.Value ?? 0))
            });
        }

        return result;
    }

    public async Task<List<RegistryEvent>> DecodeRegistryEventsAsync(ChainDefinition chain, string registryAddress, long fromBlock, long toBlock, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(registryAddress)) return new List<RegistryEvent>();
        cancellationToken.ThrowIfCancellationRequested();
        var web3 = new Web3(chain.EffectiveServerRpcUrl);
        var from = new BlockParameter((ulong)fromBlock);
        var to = new BlockParameter((ulong)toBlock);
        var result = new List<RegistryEvent>();

        var registered = web3.Eth.GetEvent<AgentRegisteredEventDTO>(registryAddress);
        foreach (var item in await registered.GetAllChangesAsync(registered.CreateFilterInput(from, to)).ConfigureAwait(false))
        {
            result.Add(new RegistryEvent
            {
                AgentId = (long)item.Event.AgentId,
                Owner = item.Event.Owner,
                MetadataURI = item.Event.MetadataURI,
                TransactionHash = item.Log.TransactionHash?.ToLowerInvariant() ?? string.Empty,
                BlockNumber = (long)(item.Log.BlockNumber?.Value ?? 0),
                LogIndex = checked((int)(item.Log.LogIndex?.Value ?? 0))
            });
        }

        return result;
    }
}
