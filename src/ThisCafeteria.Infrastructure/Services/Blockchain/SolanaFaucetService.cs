using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Application.Repositories;

namespace ThisCafeteria.Infrastructure.Services.Blockchain;

/// <summary>
/// Server-side devnet CAFE faucet: an authorized Token-2022 mint by the manifest administrator. Fails
/// closed unless the chain is an enabled Solana chain with the faucet capability AND the administrator
/// secret is present and matches the manifest. The browser never sees the secret; it only calls the
/// API. Cooldown is enforced here because, unlike the EVM faucet contract, a bare mint has none.
/// </summary>
public sealed class SolanaFaucetService(
    IChainRegistry registry,
    IHttpClientFactory httpClientFactory,
    ISolanaFaucetClaimRepository claims,
    IOptions<SolanaFaucetOptions> options,
    TimeProvider timeProvider,
    ILogger<SolanaFaucetService> logger) : ISolanaFaucetService
{
    private SolanaFaucetOptions Options => options.Value;

    public async Task<CafeFaucetStatus> GetStatusAsync(string chainKey, string wallet, CancellationToken cancellationToken = default)
    {
        if (!TryResolve(chainKey, wallet, out var chain, out _)) return new CafeFaucetStatus();
        var configured = SolanaFaucetSecret.TryLoad(Environment.GetEnvironmentVariable(SolanaFaucetSecret.EnvironmentVariable), chain.Deployment.Admin, out _, out _);
        var nextClaimAt = await NextClaimAtAsync(chain.Key, wallet, cancellationToken).ConfigureAwait(false);
        var canClaim = configured && (nextClaimAt is null || nextClaimAt <= timeProvider.GetUtcNow());
        return new CafeFaucetStatus
        {
            IsConfigured = configured,
            ClaimAmount = Options.ClaimAmount,
            CooldownSeconds = Options.CooldownSeconds,
            NextClaimAtUtc = nextClaimAt?.UtcDateTime,
            CanClaim = canClaim,
            FaucetBalance = configured ? decimal.MaxValue : 0m // an authorized mint has no fixed balance
        };
    }

    public async Task<SolanaFaucetClaimResult> ClaimAsync(string chainKey, string wallet, CancellationToken cancellationToken = default)
    {
        if (!TryResolve(chainKey, wallet, out var chain, out var walletKey)) return Failure("A valid enabled Solana faucet chain and wallet are required.");
        if (!SolanaFaucetSecret.TryLoad(Environment.GetEnvironmentVariable(SolanaFaucetSecret.EnvironmentVariable), chain.Deployment.Admin, out var secret, out var secretError))
            return Failure(secretError ?? "The Solana faucet is not configured.");

        var nextClaimAt = await NextClaimAtAsync(chain.Key, wallet, cancellationToken).ConfigureAwait(false);
        if (nextClaimAt is not null && nextClaimAt > timeProvider.GetUtcNow())
            return new SolanaFaucetClaimResult(false, null, null, Options.ClaimAmount, "This wallet already claimed recently. Try again later.", nextClaimAt.Value.UtcDateTime);

        var decimals = chain.Deployment.CafeDecimals;
        var rawAmount = (BigInteger)decimal.Truncate(Options.ClaimAmount * (decimal)BigInteger.Pow(10, decimals));
        if (rawAmount <= 0 || rawAmount > ulong.MaxValue) return Failure("The configured faucet claim amount is invalid.");

        var mint = SolanaTransactionBuilder.DecodeKey(chain.Deployment.Cafe);
        var tokenProgram = SolanaTransactionBuilder.DecodeKey(chain.Deployment.Token2022Program);
        var authority = SolanaTransactionBuilder.DecodeKey(chain.Deployment.Admin);
        var ata = SolanaTransactionBuilder.DeriveAssociatedTokenAccount(walletKey, mint, tokenProgram);

        string signature;
        try
        {
            var blockhash = await GetLatestBlockhashAsync(chain, cancellationToken).ConfigureAwait(false);
            var create = SolanaTransactionBuilder.CreateAssociatedTokenAccountIdempotent(authority, ata, walletKey, mint, tokenProgram);
            var mintTo = SolanaTransactionBuilder.MintToChecked(mint, ata, authority, tokenProgram, (ulong)rawAmount, (byte)decimals);
            var (message, _) = SolanaTransactionBuilder.CompileMessage(authority, blockhash, new[] { create, mintTo });
            var wireTransaction = SolanaTransactionBuilder.SignTransaction(message, secret);
            signature = await SendTransactionAsync(chain, wireTransaction, cancellationToken).ConfigureAwait(false);
            await ConfirmAsync(chain, signature, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Solana faucet claim failed for {ChainKey}/{Wallet}", chain.Key, wallet);
            return Failure("The devnet CAFE mint could not be completed. Please try again.");
        }

        await claims.AddAsync(
            new SolanaFaucetClaim
            {
                ChainKey = chain.Key,
                WalletAddress = wallet,
                Amount = Options.ClaimAmount,
                RawAmount = (long)rawAmount,
                Signature = signature,
                ClaimedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            },
            cancellationToken).ConfigureAwait(false);

        var explorerUrl = string.Format(CultureInfo.InvariantCulture, chain.ExplorerTransactionTemplate, signature);
        return new SolanaFaucetClaimResult(true, signature, explorerUrl, Options.ClaimAmount, null, null);
    }

    private bool TryResolve(string chainKey, string wallet, out ChainDefinition chain, out byte[] walletKey)
    {
        walletKey = Array.Empty<byte>();
        return registry.TryGet(chainKey, out chain!)
            && chain.Enabled
            && chain.Family == ChainFamily.Solana
            && chain.Capabilities.Faucet
            && SolanaBase58.TryDecode(wallet, out walletKey!)
            && walletKey.Length == 32;
    }

    private async Task<DateTimeOffset?> NextClaimAtAsync(string chainKey, string wallet, CancellationToken cancellationToken)
    {
        var last = await claims.FindLatestAsync(chainKey, wallet, cancellationToken).ConfigureAwait(false);
        return last is null ? null : new DateTimeOffset(last.ClaimedAtUtc, TimeSpan.Zero).AddSeconds(Options.CooldownSeconds);
    }

    private async Task<byte[]> GetLatestBlockhashAsync(ChainDefinition chain, CancellationToken cancellationToken)
    {
        var result = await RpcAsync(chain, "getLatestBlockhash", new object[] { new { commitment = "finalized" } }, cancellationToken).ConfigureAwait(false);
        var blockhash = result.GetProperty("value").GetProperty("blockhash").GetString();
        if (blockhash is null || !SolanaBase58.TryDecode(blockhash, out var bytes) || bytes.Length != 32) throw new InvalidOperationException("The Solana node returned an invalid blockhash.");
        return bytes;
    }

    private async Task<string> SendTransactionAsync(ChainDefinition chain, string wireTransaction, CancellationToken cancellationToken)
    {
        var result = await RpcAsync(chain, "sendTransaction", new object[] { wireTransaction, new { encoding = "base64", preflightCommitment = "confirmed" } }, cancellationToken).ConfigureAwait(false);
        return result.GetString() ?? throw new InvalidOperationException("The Solana node did not return a signature.");
    }

    private async Task ConfirmAsync(ChainDefinition chain, string signature, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var result = await RpcAsync(chain, "getSignatureStatuses", new object[] { new[] { signature }, new { searchTransactionHistory = true } }, cancellationToken).ConfigureAwait(false);
            var status = result.GetProperty("value")[0];
            if (status.ValueKind != JsonValueKind.Null)
            {
                if (status.TryGetProperty("err", out var err) && err.ValueKind != JsonValueKind.Null) throw new InvalidOperationException($"The mint transaction failed on-chain: {err}");
                var confirmation = status.TryGetProperty("confirmationStatus", out var confirmationStatus) ? confirmationStatus.GetString() : null;
                if (confirmation is "confirmed" or "finalized") return;
            }
            await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("The mint transaction was not confirmed in time.");
    }

    private async Task<JsonElement> RpcAsync(ChainDefinition chain, string method, object[] parameters, CancellationToken cancellationToken)
    {
        using var response = await httpClientFactory.CreateClient().PostAsJsonAsync(chain.EffectiveServerRpcUrl, new { jsonrpc = "2.0", id = 1, method, @params = parameters }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        if (document.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.ToString());
        return document.RootElement.GetProperty("result").Clone();
    }

    private SolanaFaucetClaimResult Failure(string error) => new(false, null, null, Options.ClaimAmount, error, null);
}
