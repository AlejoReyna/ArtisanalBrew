using System.Globalization;
using System.Numerics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.Infrastructure.Services.Reconciliation;

/// <summary>
/// Projects finalized Solana Anchor events. Solana's cursor is a signature watermark plus
/// slot, not an EVM-style contiguous block number: finalized signatures are paged newest-first,
/// and the complete bounded page set is committed before the watermark moves.
/// </summary>
public sealed class SolanaReconciliationSupervisor(
    IServiceScopeFactory scopeFactory,
    IChainRegistry registry,
    IHttpClientFactory httpClientFactory,
    ILogger<SolanaReconciliationSupervisor> logger) : BackgroundService
{
    public sealed record BackfillReport(string ChainKey, long StartSlot, long EndSlot, bool DryRun, bool AdvancedLiveCursor, int Fetched, int Decoded, int Inserted, int Existing);
    private const int PageSize = 1000;
    private const int MaxPagesPerRun = 10;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var chains = registry.All.Where(item => item.Enabled && item.Family == ChainFamily.Solana && item.Capabilities.LiquidStaking && SolanaBase58.IsPublicKey(item.Deployment.Program)).ToArray();
        if (chains.Length == 0) return;
        await Task.WhenAll(chains.Select(chain => RunChainAsync(chain, stoppingToken))).ConfigureAwait(false);
    }

    public async Task<BackfillReport> BackfillAsync(string chainKey, long startSlot, long endSlot, bool dryRun, bool allowLargeRange, bool advanceLiveCursor, CancellationToken token)
    {
        if (startSlot < 0 || endSlot < startSlot) throw new ArgumentOutOfRangeException(nameof(startSlot), "The inclusive slot range is invalid.");
        if (endSlot - startSlot > 100_000 && !allowLargeRange) throw new ArgumentOutOfRangeException(nameof(endSlot), "Backfill range exceeds 100,000 slots; use --allow-large-range after reviewing the range.");
        if (!registry.TryGet(chainKey, out var chain) || !chain.Enabled || chain.Family != ChainFamily.Solana || !chain.Capabilities.LiquidStaking) throw new InvalidOperationException("The requested Solana chain is not enabled for liquid staking.");
        var signatures = await ReadSignaturesRangeAsync(chain, startSlot, endSlot, token).ConfigureAwait(false);
        var projections = new List<StakingLedgerEntry>();
        foreach (var signature in signatures.OrderBy(item => item.Slot).ThenBy(item => item.Signature, StringComparer.Ordinal))
        {
            var transaction = await RpcAsync(chain, "getTransaction", new object[] { signature.Signature, new { commitment = "finalized", encoding = "json", maxSupportedTransactionVersion = 0 } }, token).ConfigureAwait(false);
            if (transaction.ValueKind == JsonValueKind.Null) throw new InvalidOperationException($"Finalized transaction '{signature.Signature}' was temporarily unavailable; the backfill was not committed.");
            if (transaction.GetProperty("meta").GetProperty("err").ValueKind != JsonValueKind.Null) continue;
            projections.AddRange(Decode(chain, signature.Signature, signature.Slot, transaction));
        }
        if (dryRun) return new BackfillReport(chainKey, startSlot, endSlot, true, false, signatures.Count, projections.Count, 0, 0);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transactionScope = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        var inserted = 0;
        var existing = 0;
        foreach (var entry in projections)
        {
            var found = await db.StakingLedgerEntries.AnyAsync(item => item.ChainKey == entry.ChainKey && item.TransactionHash == entry.TransactionHash && item.OperationIndex == entry.OperationIndex, token).ConfigureAwait(false);
            if (found) { existing++; continue; }
            db.StakingLedgerEntries.Add(entry); inserted++;
        }
        if (advanceLiveCursor)
        {
            var checkpoint = await db.StakingReconciliationCheckpoints.SingleOrDefaultAsync(item => item.ChainKey == chain.Key && item.SourceIdentifier == chain.Deployment.Program, token).ConfigureAwait(false);
            if (checkpoint is null)
            {
                checkpoint = new StakingReconciliationCheckpoint { ChainKey = chain.Key, Family = chain.FamilyName, SourceIdentifier = chain.Deployment.Program, StakingPoolContract = chain.Deployment.Program, CursorType = "signature" };
                db.StakingReconciliationCheckpoints.Add(checkpoint);
            }
            checkpoint.LastScannedSlot = Math.Max(checkpoint.LastScannedSlot, endSlot);
            checkpoint.LastScannedSignature = signatures.OrderByDescending(item => item.Slot).FirstOrDefault()?.Signature ?? checkpoint.LastScannedSignature;
            checkpoint.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(token).ConfigureAwait(false);
        await transactionScope.CommitAsync(token).ConfigureAwait(false);
        return new BackfillReport(chainKey, startSlot, endSlot, false, advanceLiveCursor, signatures.Count, projections.Count, inserted, existing);
    }

    private async Task RunChainAsync(ChainDefinition chain, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await ReconcileOnceAsync(chain, token).ConfigureAwait(false); await Task.Delay(PollInterval, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Solana reconciliation failed for {ChainKey}; signature cursor was not advanced", chain.Key);
                try { await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
        }
    }

    internal async Task ReconcileOnceAsync(ChainDefinition chain, CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = chain.Deployment.Program;
        var checkpoint = await db.StakingReconciliationCheckpoints.SingleOrDefaultAsync(item => item.ChainKey == chain.Key && item.SourceIdentifier == source, token).ConfigureAwait(false);
        if (checkpoint is null)
        {
            checkpoint = new StakingReconciliationCheckpoint { ChainKey = chain.Key, Family = chain.FamilyName, SourceIdentifier = source, StakingPoolContract = source, CursorType = "signature", LastScannedSlot = chain.Deployment.StartBlockOrSlot - 1 };
            db.StakingReconciliationCheckpoints.Add(checkpoint);
        }

        var signatures = await ReadSignaturesAsync(chain, checkpoint.LastScannedSignature, checkpoint.LastScannedSlot, token).ConfigureAwait(false);
        if (signatures.Count == 0) return;
        var projections = new List<StakingLedgerEntry>();
        foreach (var signature in signatures.OrderBy(item => item.Slot).ThenBy(item => item.Signature, StringComparer.Ordinal))
        {
            var transaction = await RpcAsync(chain, "getTransaction", new object[] { signature.Signature, new { commitment = "finalized", encoding = "json", maxSupportedTransactionVersion = 0 } }, token).ConfigureAwait(false);
            if (transaction.ValueKind == JsonValueKind.Null) throw new InvalidOperationException($"Finalized transaction '{signature.Signature}' was temporarily unavailable; the reconciliation cursor was not advanced.");
            if (transaction.GetProperty("meta").GetProperty("err").ValueKind != JsonValueKind.Null) continue;
            projections.AddRange(Decode(chain, signature.Signature, signature.Slot, transaction));
        }

        await using var transactionScope = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
        foreach (var entry in projections)
        {
            var exists = await db.StakingLedgerEntries.AnyAsync(item => item.ChainKey == entry.ChainKey && item.TransactionHash == entry.TransactionHash && item.OperationIndex == entry.OperationIndex, token).ConfigureAwait(false);
            if (!exists) db.StakingLedgerEntries.Add(entry);
        }
        var newest = signatures.OrderByDescending(item => item.Slot).First();
        checkpoint.LastScannedSlot = Math.Max(checkpoint.LastScannedSlot, newest.Slot);
        checkpoint.LastScannedSignature = newest.Signature;
        checkpoint.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token).ConfigureAwait(false);
        await transactionScope.CommitAsync(token).ConfigureAwait(false);
    }

    internal async Task<List<(string Signature, long Slot)>> ReadSignaturesForTestAsync(ChainDefinition chain, string cursor, long startSlot, CancellationToken token) =>
        (await ReadSignaturesAsync(chain, cursor, startSlot, token).ConfigureAwait(false)).Select(item => (item.Signature, item.Slot)).ToList();

    private async Task<List<SolanaSignature>> ReadSignaturesAsync(ChainDefinition chain, string cursor, long startSlot, CancellationToken token)
    {
        var result = new List<SolanaSignature>();
        string? before = null;
        var completed = false;
        for (var page = 0; page < MaxPagesPerRun; page++)
        {
            var parameters = new List<object> { chain.Deployment.Program, new { limit = PageSize, commitment = "finalized", until = string.IsNullOrWhiteSpace(cursor) ? null : cursor, before } };
            var rows = await RpcAsync(chain, "getSignaturesForAddress", parameters.ToArray(), token).ConfigureAwait(false);
            var raw = rows.EnumerateArray().Select(row => new SolanaSignature(row.GetProperty("signature").GetString() ?? string.Empty, row.GetProperty("slot").GetInt64())).ToArray();
            result.AddRange(raw.Where(row => row.Slot >= startSlot));
            if (raw.Length == 0 || raw.Length < PageSize || raw[^1].Slot < startSlot) { completed = true; break; }
            before = raw[^1].Signature;
        }
        if (!completed) throw new InvalidOperationException($"Solana reconciliation exceeded {MaxPagesPerRun * PageSize:N0} signatures; run a bounded backfill before advancing the live cursor.");
        result.RemoveAll(item => item.Slot < startSlot);
        return result.GroupBy(item => item.Signature, StringComparer.Ordinal).Select(group => group.First()).ToList();
    }

    private async Task<List<SolanaSignature>> ReadSignaturesRangeAsync(ChainDefinition chain, long startSlot, long endSlot, CancellationToken token)
    {
        var result = new List<SolanaSignature>();
        string? before = null;
        var completed = false;
        for (var page = 0; page < 100; page++)
        {
            var config = new { limit = PageSize, commitment = "finalized", before };
            var rows = await RpcAsync(chain, "getSignaturesForAddress", new object[] { chain.Deployment.Program, config }, token).ConfigureAwait(false);
            var raw = rows.EnumerateArray().Select(row => new SolanaSignature(row.GetProperty("signature").GetString() ?? string.Empty, row.GetProperty("slot").GetInt64())).ToArray();
            if (raw.Length == 0) { completed = true; break; }
            result.AddRange(raw.Where(item => item.Slot >= startSlot && item.Slot <= endSlot));
            if (raw[^1].Slot < startSlot || raw.Length < PageSize) { completed = true; break; }
            before = raw[^1].Signature;
        }
        if (!completed) throw new InvalidOperationException("Solana backfill pagination did not reach the requested start slot; no projection was committed.");
        return result.GroupBy(item => item.Signature, StringComparer.Ordinal).Select(group => group.First()).ToList();
    }

    private async Task<JsonElement> RpcAsync(ChainDefinition chain, string method, object[] parameters, CancellationToken token)
    {
        using var response = await httpClientFactory.CreateClient().PostAsJsonAsync(chain.EffectiveServerRpcUrl, new { jsonrpc = "2.0", id = 1, method, @params = parameters }, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false));
        if (document.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException($"Solana RPC error: {error}");
        return document.RootElement.GetProperty("result").Clone();
    }

    internal static IEnumerable<StakingLedgerEntry> Decode(ChainDefinition chain, string signature, long slot, JsonElement transaction)
    {
        var events = SolanaAnchorEventCodec.Decode(
            transaction.GetProperty("meta").GetProperty("logMessages"),
            chain.Deployment.Program,
            SolanaAnchorEventCodec.Deposit,
            SolanaAnchorEventCodec.Redeem,
            SolanaAnchorEventCodec.RewardClaimed,
            SolanaAnchorEventCodec.RewardFunded,
            SolanaAnchorEventCodec.TransferCheckpoint);
        foreach (var anchorEvent in events)
        {
            var data = anchorEvent.Payload;
            var action = anchorEvent.Name switch
            {
                SolanaAnchorEventCodec.Deposit => "deposit",
                SolanaAnchorEventCodec.Redeem => "redeem",
                SolanaAnchorEventCodec.RewardClaimed => "claim",
                SolanaAnchorEventCodec.RewardFunded => "reward_funding",
                SolanaAnchorEventCodec.TransferCheckpoint => "transfer",
                _ => throw new InvalidOperationException("Unsupported Solana event.")
            };
            var wallet = action == "reward_funding" ? chain.Deployment.Admin : SolanaBase58.Encode(data.AsSpan(0, 32));
            var assets = action is "deposit" or "redeem" ? ReadU64(data, 32) : 0UL;
            var shares = action is "deposit" or "redeem" ? ReadU64(data, 40) : action == "transfer" ? ReadU64(data, 64) : 0UL;
            var reward = action == "claim" ? ReadU64(data, 32) : action == "reward_funding" ? ReadU64(data, 0) : 0UL;
            yield return StakingLedgerEntry.Create(chain.Key, signature, anchorEvent.LogIndex, entry =>
            {
                entry.WalletAddress = wallet;
                entry.Family = chain.FamilyName;
                entry.ActionType = action;
                entry.Amount = action is "claim" or "reward_funding" ? FromRaw(reward, chain.Deployment.CoffeeDecimals) : FromRaw(assets, chain.Deployment.CafeDecimals);
                entry.AssetAmount = FromRaw(assets, chain.Deployment.CafeDecimals);
                entry.ShareAmount = FromRaw(shares, chain.Deployment.StCafeDecimals);
                entry.RewardAmount = FromRaw(reward, chain.Deployment.CoffeeDecimals);
                entry.RawAssetAmount = assets.ToString(CultureInfo.InvariantCulture);
                entry.RawShareAmount = shares.ToString(CultureInfo.InvariantCulture);
                entry.RawRewardAmount = reward.ToString(CultureInfo.InvariantCulture);
                entry.NetworkName = chain.DisplayName;
                entry.PaymentTokenContract = chain.Deployment.Cafe;
                entry.StakingPoolContract = chain.Deployment.Program;
                entry.AssetIdentifier = chain.Deployment.Cafe;
                entry.ReceiptIdentifier = chain.Deployment.StCafe;
                entry.RewardIdentifier = chain.Deployment.Coffee;
                entry.VaultOrProgramIdentifier = chain.Deployment.Program;
                entry.BlockOrSlot = slot;
                entry.VerificationState = "reconciled";
                entry.Verified = true;
                entry.RecordedAtUtc = DateTime.UtcNow;
                entry.OccurredAtUtc = DateTime.UtcNow;
                entry.ExplorerUrl = string.Format(chain.ExplorerTransactionTemplate, signature);
            });
        }
    }

    private static ulong ReadU64(byte[] data, int offset) => BitConverter.ToUInt64(data, offset);
    private static decimal FromRaw(ulong value, int decimals) => value / (decimal)BigInteger.Pow(10, decimals);
    private sealed record SolanaSignature(string Signature, long Slot);
}
