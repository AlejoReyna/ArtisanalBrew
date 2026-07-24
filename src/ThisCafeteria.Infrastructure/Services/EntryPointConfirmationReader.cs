using System.Numerics;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Infrastructure.Services;

/// <summary>
/// See <see cref="IEntryPointConfirmationReader"/>. Reads the canonical EntryPoint's own
/// <c>UserOperationEvent</c> from the chain node with read-only calls only — it never signs, never
/// submits, and never calls the bundler. Both confirmation paths end by decoding the mined
/// transaction's real receipt and matching the EntryPoint address, sender, and userOpHash exactly,
/// the same event-decode-and-match pattern <c>EvmLiquidStakingGateway</c> uses for escrow and
/// liquid-staking transactions.
/// </summary>
public sealed class EntryPointConfirmationReader(IChainRegistry chains) : IEntryPointConfirmationReader
{
    // A fresh submission is mined within the submitter's ~60s poll window (a handful of blocks on any
    // mainnet-cadence chain). Locating it by an indexed-topic log query over a bounded recent window
    // is cheap for the node to serve and never triggers the full-history scan that breaks Rundler's
    // own receipt endpoint.
    private const int EventLookbackBlocks = 10_000;

    [Event("UserOperationEvent")]
    private sealed class UserOperationEventDto : IEventDTO
    {
        [Parameter("bytes32", "userOpHash", 1, true)] public byte[] UserOpHash { get; set; } = [];
        [Parameter("address", "sender", 2, true)] public string Sender { get; set; } = string.Empty;
        [Parameter("address", "paymaster", 3, true)] public string Paymaster { get; set; } = string.Empty;
        [Parameter("uint256", "nonce", 4, false)] public BigInteger Nonce { get; set; }
        [Parameter("bool", "success", 5, false)] public bool Success { get; set; }
        [Parameter("uint256", "actualGasCost", 6, false)] public BigInteger ActualGasCost { get; set; }
        [Parameter("uint256", "actualGasUsed", 7, false)] public BigInteger ActualGasUsed { get; set; }
    }

    public async Task<EntryPointConfirmation?> FindConfirmationAsync(
        string chainKey, string sender, string userOpHash, string? transactionHashHint, CancellationToken cancellationToken = default)
    {
        var chain = chains.GetRequired(chainKey);
        if (string.IsNullOrWhiteSpace(chain.Deployment.EntryPoint))
            throw new NotSupportedException($"No trusted EntryPoint is configured for chain '{chainKey}'.");

        var web3 = new Web3(chain.EffectiveServerRpcUrl);

        var transactionHash = string.IsNullOrWhiteSpace(transactionHashHint)
            ? await LocateTransactionByUserOpHashAsync(web3, chain, userOpHash).ConfigureAwait(false)
            : transactionHashHint;
        if (string.IsNullOrWhiteSpace(transactionHash)) return null;

        return await VerifyByTransactionAsync(web3, chain, sender, userOpHash, transactionHash!).ConfigureAwait(false);
    }

    // Independent of the bundler: query the trusted EntryPoint's own UserOperationEvent logs by the
    // indexed userOpHash topic over a bounded recent window. This is the same read the bundler's
    // receipt endpoint performs internally, but scoped to a range the node can serve quickly.
    private static async Task<string?> LocateTransactionByUserOpHashAsync(Web3 web3, ChainDefinition chain, string userOpHash)
    {
        var latest = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync().ConfigureAwait(false);
        var from = BigInteger.Max(BigInteger.Zero, latest.Value - EventLookbackBlocks);

        var handler = web3.Eth.GetEvent<UserOperationEventDto>(chain.Deployment.EntryPoint);
        var filter = handler.CreateFilterInput(
            new object[] { userOpHash.HexToByteArray() },
            new BlockParameter(new HexBigInteger(from)),
            BlockParameter.CreateLatest());
        var logs = await handler.GetAllChangesAsync(filter).ConfigureAwait(false);
        return logs.FirstOrDefault()?.Log.TransactionHash;
    }

    private static async Task<EntryPointConfirmation?> VerifyByTransactionAsync(Web3 web3, ChainDefinition chain, string sender, string userOpHash, string transactionHash)
    {
        var receipt = await web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(transactionHash).ConfigureAwait(false);
        var eventLog = receipt?.DecodeAllEvents<UserOperationEventDto>().FirstOrDefault(item =>
            AddressMatches(item.Log.Address, chain.Deployment.EntryPoint) &&
            AddressMatches(item.Event.Sender, sender) &&
            ToHex(item.Event.UserOpHash).Equals(userOpHash, StringComparison.OrdinalIgnoreCase));
        return eventLog is null
            ? null
            : new EntryPointConfirmation { TransactionHash = transactionHash, Success = eventLog.Event.Success };
    }

    private static bool AddressMatches(string? actual, string expected) =>
        !string.IsNullOrWhiteSpace(actual) && actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string ToHex(byte[] bytes) => "0x" + Convert.ToHexString(bytes).ToLowerInvariant();
}
