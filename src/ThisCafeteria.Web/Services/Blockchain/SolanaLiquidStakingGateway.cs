using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.Web.Services.Blockchain;

public sealed class SolanaLiquidStakingGateway(
    IChainRegistry registry,
    IHttpClientFactory httpClientFactory,
    ILogger<SolanaLiquidStakingGateway> logger) : ILiquidStakingGateway
{
    private static readonly BigInteger RewardScale = BigInteger.Pow(10, 18);

    public async Task<LiquidStakingDashboard> GetDashboardAsync(string chainKey, string walletIdentifier, CancellationToken cancellationToken = default)
    {
        if (!TryGetConfiguredChain(chainKey, out var chain) || !SolanaBase58.TryDecode(walletIdentifier, out var walletBytes) || walletBytes.Length != 32)
            return Unavailable(chainKey, walletIdentifier, "Solana liquid staking is not configured for this chain.");
        try
        {
            var balance = await RpcAsync(chain, "getBalance", new object[] { walletIdentifier, new { commitment = chain.SolanaCommitment } }, cancellationToken).ConfigureAwait(false);
            var native = balance.GetProperty("value").GetInt64();
            var cafeRaw = await TokenBalanceRawAsync(chain, walletIdentifier, chain.Deployment.Cafe, cancellationToken).ConfigureAwait(false);
            var sharesRaw = await TokenBalanceRawAsync(chain, walletIdentifier, chain.Deployment.StCafe, cancellationToken).ConfigureAwait(false);
            var coffeeRaw = await TokenBalanceRawAsync(chain, walletIdentifier, chain.Deployment.Coffee, cancellationToken).ConfigureAwait(false);
            var cafeDecimals = await ReadMintDecimalsAsync(chain, chain.Deployment.Cafe, cancellationToken).ConfigureAwait(false);
            var stCafeDecimals = await ReadMintDecimalsAsync(chain, chain.Deployment.StCafe, cancellationToken).ConfigureAwait(false);
            var coffeeDecimals = await ReadMintDecimalsAsync(chain, chain.Deployment.Coffee, cancellationToken).ConfigureAwait(false);
            var vault = await ReadVaultStateAsync(chain, cancellationToken).ConfigureAwait(false);
            RequireDecimals(chain, vault, cafeDecimals, stCafeDecimals, coffeeDecimals);
            var position = await ReadPositionAsync(chain, walletIdentifier, cancellationToken).ConfigureAwait(false);
            var currentRewardPerShare = vault.TotalShares == 0 || vault.RewardRate == 0 ? vault.RewardPerShare : vault.RewardPerShare + new BigInteger(Math.Max(0, Math.Min(vault.PeriodFinish, vault.CurrentSlot) - vault.LastRewardSlot)) * vault.RewardRate * RewardScale / vault.TotalShares;
            var pendingRaw = position is null || currentRewardPerShare < position.RewardPerSharePaid ? BigInteger.Zero : position.PendingRewards + position.Shares * (currentRewardPerShare - position.RewardPerSharePaid) / RewardScale;
            var vaultCafeRaw = await TokenBalanceRawAsync(chain, chain.Deployment.VaultPda, chain.Deployment.Cafe, cancellationToken).ConfigureAwait(false);
            return new LiquidStakingDashboard
            {
                ChainKey = chain.Key,
                Family = chain.FamilyName,
                WalletIdentifier = walletIdentifier,
                IsConfigured = true,
                CafeBalance = FromRaw(cafeRaw, cafeDecimals),
                StCafeBalance = FromRaw(sharesRaw, stCafeDecimals),
                RedeemableCafe = FromRaw(sharesRaw, cafeDecimals),
                ExchangeRate = 1m,
                PendingCoffee = FromRaw(pendingRaw, coffeeDecimals),
                CoffeeBalance = FromRaw(coffeeRaw, coffeeDecimals),
                DepositPreviewShares = 1m,
                NativeGasBalance = native / 1_000_000_000m,
                VaultIdentifier = chain.Deployment.VaultPda,
                AssetIdentifier = chain.Deployment.Cafe,
                ReceiptIdentifier = chain.Deployment.StCafe,
                RewardIdentifier = chain.Deployment.Coffee
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Solana dashboard read failed for {ChainKey}", chainKey);
            return Unavailable(chain.Key, walletIdentifier, "The configured Solana RPC is unavailable.");
        }
    }

    public async Task<LiquidTransactionVerificationResult> VerifyAsync(string chainKey, string walletIdentifier, string transactionId, LiquidStakingOperation operation, decimal? expectedAmount, CancellationToken cancellationToken = default)
    {
        if (!TryGetConfiguredChain(chainKey, out var chain) || !SolanaBase58.TryDecode(walletIdentifier, out var walletBytes) || walletBytes.Length != 32 || !SolanaBase58.TryDecode(transactionId, out var signatureBytes) || signatureBytes.Length != 64)
            return LiquidTransactionVerificationResult.Failed("Solana chain, wallet, or signature is not valid or configured.");
        try
        {
            var transaction = await RpcAsync(chain, "getTransaction", new object[] { transactionId, new { commitment = "finalized", encoding = "json", maxSupportedTransactionVersion = 0 } }, cancellationToken).ConfigureAwait(false);
            if (transaction.ValueKind == JsonValueKind.Null) return LiquidTransactionVerificationResult.Failed("The Solana transaction was not found.");
            if (!transaction.TryGetProperty("meta", out var meta) || !meta.TryGetProperty("err", out var err) || err.ValueKind != JsonValueKind.Null)
                return LiquidTransactionVerificationResult.Failed("The Solana transaction failed.");
            var slot = transaction.GetProperty("slot").GetInt64();
            var message = transaction.GetProperty("transaction").GetProperty("message");
            var accountKeys = message.GetProperty("accountKeys").EnumerateArray().Select(KeyString).Where(item => item is not null).Cast<string>().ToList();
            if (meta.TryGetProperty("loadedAddresses", out var loaded))
            {
                accountKeys.AddRange(loaded.GetProperty("writable").EnumerateArray().Select(item => item.GetString() ?? string.Empty));
                accountKeys.AddRange(loaded.GetProperty("readonly").EnumerateArray().Select(item => item.GetString() ?? string.Empty));
            }
            if (!accountKeys.Contains(chain.Deployment.Program, StringComparer.Ordinal)) return LiquidTransactionVerificationResult.Failed("The transaction did not invoke the trusted Solana program.");
            var requiredSigners = message.GetProperty("header").GetProperty("numRequiredSignatures").GetInt32();
            if (requiredSigners < 1 || !string.Equals(accountKeys[0], walletIdentifier, StringComparison.Ordinal)) return LiquidTransactionVerificationResult.Failed("The authenticated wallet was not the transaction signer.");
            var expectedInstruction = operation switch { LiquidStakingOperation.Deposit => "deposit", LiquidStakingOperation.Redeem => "redeem", LiquidStakingOperation.Claim => "claim_rewards", LiquidStakingOperation.RewardFunding => "fund_rewards", _ => string.Empty };
            var instruction = FindInstruction(message, accountKeys, chain.Deployment.Program, expectedInstruction, out var instructionIndex);
            if (instruction.ValueKind == JsonValueKind.Undefined) return LiquidTransactionVerificationResult.Failed("The transaction instruction did not match the requested operation.");
            RequireAccounts(chain, operation, walletIdentifier, accountKeys, instruction, meta);
            var events = DecodeEvents(meta, chain.Deployment.Program, operation);
            var selected = events.FirstOrDefault(item => operation == LiquidStakingOperation.RewardFunding || item.Owner.Equals(walletIdentifier, StringComparison.Ordinal));
            if (selected is null) return LiquidTransactionVerificationResult.Failed("No matching Anchor event was observed.");
            if (expectedAmount is not null)
            {
                var decimals = operation == LiquidStakingOperation.Claim || operation == LiquidStakingOperation.RewardFunding
                    ? await ReadMintDecimalsAsync(chain, chain.Deployment.Coffee, cancellationToken).ConfigureAwait(false)
                    : await ReadMintDecimalsAsync(chain, chain.Deployment.Cafe, cancellationToken).ConfigureAwait(false);
                var raw = ToRaw(expectedAmount.Value, decimals);
                var actual = operation switch
                {
                    LiquidStakingOperation.Claim or LiquidStakingOperation.RewardFunding => selected.Reward,
                    LiquidStakingOperation.Deposit => selected.Assets,
                    _ => selected.Shares
                };
                if (actual != raw) return LiquidTransactionVerificationResult.Failed("The verified amount did not match the submitted intent.");
            }
            var assetDecimals = await ReadMintDecimalsAsync(chain, chain.Deployment.Cafe, cancellationToken).ConfigureAwait(false);
            var shareDecimals = await ReadMintDecimalsAsync(chain, chain.Deployment.StCafe, cancellationToken).ConfigureAwait(false);
            var rewardDecimals = await ReadMintDecimalsAsync(chain, chain.Deployment.Coffee, cancellationToken).ConfigureAwait(false);
            if (assetDecimals != chain.Deployment.CafeDecimals || shareDecimals != chain.Deployment.StCafeDecimals ||
                rewardDecimals != chain.Deployment.CoffeeDecimals || assetDecimals != shareDecimals)
                return LiquidTransactionVerificationResult.Failed("The on-chain mint decimals do not match the verified deployment manifest.");
            return new LiquidTransactionVerificationResult(true, TransactionVerificationStatus.Verified,
                AssetAmount: FromRaw(selected.Assets, assetDecimals), ShareAmount: FromRaw(selected.Shares, shareDecimals), RewardAmount: FromRaw(selected.Reward, rewardDecimals),
                BlockNumber: slot, OperationIndex: selected.Index);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Solana transaction verification failed for {ChainKey}/{TransactionId}", chainKey, transactionId);
            return LiquidTransactionVerificationResult.Failed("The Solana transaction could not be verified.");
        }
    }

    private bool TryGetConfiguredChain(string key, out ChainDefinition chain) =>
        registry.TryGet(key, out chain!) && chain.Enabled && chain.Family == ChainFamily.Solana && chain.Capabilities.LiquidStaking &&
        SolanaBase58.IsPublicKey(chain.Deployment.Program) && SolanaBase58.IsPublicKey(chain.Deployment.VaultPda) &&
        SolanaBase58.IsPublicKey(chain.Deployment.Cafe) && SolanaBase58.IsPublicKey(chain.Deployment.StCafe) && SolanaBase58.IsPublicKey(chain.Deployment.Coffee) &&
        SolanaBase58.IsPublicKey(chain.Deployment.CafeCustody) && SolanaBase58.IsPublicKey(chain.Deployment.CoffeeCustody) && SolanaBase58.IsPublicKey(chain.Deployment.Admin) &&
        SolanaBase58.IsPublicKey(chain.Deployment.TokenProgram) && SolanaBase58.IsPublicKey(chain.Deployment.Token2022Program) &&
        string.Equals(chain.Deployment.AuthorityPda, chain.Deployment.VaultPda, StringComparison.Ordinal) &&
        SolanaBase58.TryDecode(chain.Deployment.VaultPda, out var vaultBytes) && PdaMatches(Array.Empty<byte>(), vaultBytes, "cafe-liquid-vault-v1", chain.Deployment.Program);

    private async Task<ulong> TokenBalanceRawAsync(ChainDefinition chain, string wallet, string mint, CancellationToken cancellationToken)
    {
        var result = await RpcAsync(chain, "getTokenAccountsByOwner", new object[] { wallet, new { mint }, new { commitment = chain.SolanaCommitment, encoding = "jsonParsed" } }, cancellationToken).ConfigureAwait(false);
        BigInteger total = 0;
        foreach (var item in result.GetProperty("value").EnumerateArray())
        {
            var amount = item.GetProperty("account").GetProperty("data").GetProperty("parsed").GetProperty("info").GetProperty("tokenAmount").GetProperty("amount").GetString() ?? "0";
            total += BigInteger.Parse(amount, CultureInfo.InvariantCulture);
        }
        return checked((ulong)total);
    }

    private async Task<int> ReadMintDecimalsAsync(ChainDefinition chain, string mint, CancellationToken cancellationToken)
    {
        var result = await RpcAsync(chain, "getAccountInfo", new object[] { mint, new { commitment = "finalized", encoding = "base64" } }, cancellationToken).ConfigureAwait(false);
        if (result.GetProperty("value").ValueKind == JsonValueKind.Null) throw new InvalidOperationException("The configured Solana mint does not exist.");
        var value = result.GetProperty("value");
        var owner = value.GetProperty("owner").GetString() ?? string.Empty;
        if (owner != chain.Deployment.Token2022Program)
            throw new InvalidOperationException("The configured Solana liquid-staking mints must be owned by Token-2022.");
        var bytes = Convert.FromBase64String(value.GetProperty("data")[0].GetString() ?? string.Empty);
        var state = SolanaAccountCodec.DecodeMint(bytes);
        if (mint == chain.Deployment.StCafe)
        {
            if (!SolanaBase58.TryDecode(chain.Deployment.VaultPda, out var vault) ||
                state.MintAuthority is null || state.FreezeAuthority is null ||
                !state.MintAuthority.SequenceEqual(vault) || !state.FreezeAuthority.SequenceEqual(vault))
                throw new InvalidOperationException("The stCAFE mint and freeze authorities do not match the vault PDA.");
        }
        return state.Decimals;
    }

    private async Task<VaultState> ReadVaultStateAsync(ChainDefinition chain, CancellationToken cancellationToken)
    {
        var result = await RpcAsync(chain, "getAccountInfo", new object[] { chain.Deployment.VaultPda, new { commitment = "finalized", encoding = "base64" } }, cancellationToken).ConfigureAwait(false);
        var value = result.GetProperty("value");
        if (value.ValueKind == JsonValueKind.Null || value.GetProperty("owner").GetString() != chain.Deployment.Program)
            throw new InvalidOperationException("The configured Solana vault is not owned by the trusted program.");
        var bytes = Convert.FromBase64String(value.GetProperty("data")[0].GetString() ?? string.Empty);
        if (!bytes.AsSpan().StartsWith(SolanaAnchorEventCodec.AccountDiscriminator("Vault")))
            throw new InvalidOperationException("The configured Solana vault account discriminator is invalid.");
        var state = SolanaAccountCodec.DecodeVault(bytes);
        var currentSlot = (await RpcAsync(chain, "getSlot", new object[] { new { commitment = "finalized" } }, cancellationToken).ConfigureAwait(false)).GetInt64();
        return new VaultState(state.CafeDecimals, state.CoffeeDecimals, state.RewardPerShare, state.RewardRate, state.PeriodFinish, state.LastRewardSlot, state.TotalShares, (ulong)currentSlot);
    }

    private async Task<PositionState?> ReadPositionAsync(ChainDefinition chain, string wallet, CancellationToken cancellationToken)
    {
        var result = await RpcAsync(chain, "getProgramAccounts", new object[] { chain.Deployment.Program, new { commitment = "finalized", encoding = "base64", filters = new object[] { new { dataSize = 72 }, new { memcmp = new { offset = 8, bytes = wallet } } } } }, cancellationToken).ConfigureAwait(false);
        var item = result.EnumerateArray().FirstOrDefault();
        if (item.ValueKind == JsonValueKind.Undefined) return null;
        var bytes = Convert.FromBase64String(item.GetProperty("account").GetProperty("data")[0].GetString() ?? string.Empty);
        if (item.GetProperty("account").GetProperty("owner").GetString() != chain.Deployment.Program ||
            bytes.Length < 72 || !bytes.AsSpan().StartsWith(SolanaAnchorEventCodec.AccountDiscriminator("Position"))) return null;
        return new PositionState(ReadU64(bytes, 40), ReadU128(bytes, 48), ReadU64(bytes, 64));
    }

    private async Task<JsonElement> RpcAsync(ChainDefinition chain, string method, object[] parameters, CancellationToken cancellationToken)
    {
        using var response = await httpClientFactory.CreateClient().PostAsJsonAsync(chain.EffectiveServerRpcUrl, new { jsonrpc = "2.0", id = 1, method, @params = parameters }, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        if (document.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.ToString());
        return document.RootElement.GetProperty("result").Clone();
    }

    private static JsonElement FindInstruction(JsonElement message, IReadOnlyList<string> keys, string program, string instruction, out int index)
    {
        var discriminator = SHA256.HashData(Encoding.UTF8.GetBytes($"global:{instruction}"))[..8];
        index = 0;
        foreach (var item in message.GetProperty("instructions").EnumerateArray())
        {
            var programId = keys[item.GetProperty("programIdIndex").GetInt32()];
            if (programId == program && SolanaBase58.TryDecode(item.GetProperty("data").GetString() ?? string.Empty, out var data) && data.Length >= 8 && data[..8].SequenceEqual(discriminator)) return item;
            index++;
        }
        index = -1;
        return default;
    }

    private static void RequireAccounts(ChainDefinition chain, LiquidStakingOperation operation, string wallet, IReadOnlyList<string> keys, JsonElement instruction, JsonElement meta)
    {
        var accountIndexes = instruction.GetProperty("accounts").EnumerateArray().Select(item => item.GetInt32()).ToArray();
        var expected = operation switch
        {
            LiquidStakingOperation.Deposit => new[] { chain.Deployment.VaultPda, wallet, string.Empty, string.Empty, chain.Deployment.CafeCustody, chain.Deployment.StCafe, string.Empty, chain.Deployment.Cafe },
            LiquidStakingOperation.Redeem => new[] { chain.Deployment.VaultPda, wallet, string.Empty, chain.Deployment.CafeCustody, string.Empty, chain.Deployment.StCafe, string.Empty, chain.Deployment.Cafe },
            LiquidStakingOperation.Claim => new[] { chain.Deployment.VaultPda, wallet, string.Empty, chain.Deployment.CoffeeCustody, string.Empty, chain.Deployment.Coffee },
            _ => new[] { chain.Deployment.Admin, chain.Deployment.VaultPda, string.Empty, chain.Deployment.CoffeeCustody, chain.Deployment.Coffee }
        };
        if (accountIndexes.Length < expected.Length || expected.Select((address, position) => (address, position)).Where(item => !string.IsNullOrWhiteSpace(item.address)).Any(item => keys[accountIndexes[item.position]] != item.address)) throw new InvalidOperationException("Trusted Solana PDA, mint, custody account, or owner was not in the expected instruction position.");
        var tokenPosition = operation == LiquidStakingOperation.Deposit ? 8 : operation == LiquidStakingOperation.Redeem ? 8 : operation == LiquidStakingOperation.Claim ? 6 : 5;
        if (accountIndexes.Length <= tokenPosition || keys[accountIndexes[tokenPosition]] != chain.Deployment.Token2022Program) throw new InvalidOperationException("The instruction did not use the configured Token-2022 program.");
        if (operation != LiquidStakingOperation.RewardFunding && (!SolanaBase58.TryDecode(wallet, out var walletBytes) || !SolanaBase58.TryDecode(keys[accountIndexes[2]], out var positionBytes) || !PdaMatches(walletBytes, positionBytes, "cafe-liquid-position-v1", chain.Deployment.Program))) throw new InvalidOperationException("The position PDA did not match the authenticated wallet.");
        if (operation == LiquidStakingOperation.Deposit)
        {
            RequireTokenAccount(meta, accountIndexes[3], chain.Deployment.Cafe, wallet);
            RequireTokenAccount(meta, accountIndexes[4], chain.Deployment.Cafe, chain.Deployment.VaultPda);
            RequireTokenAccount(meta, accountIndexes[6], chain.Deployment.StCafe, wallet);
        }
        else if (operation == LiquidStakingOperation.Redeem)
        {
            RequireTokenAccount(meta, accountIndexes[3], chain.Deployment.Cafe, chain.Deployment.VaultPda);
            RequireTokenAccount(meta, accountIndexes[4], chain.Deployment.Cafe, wallet);
            RequireTokenAccount(meta, accountIndexes[6], chain.Deployment.StCafe, wallet);
        }
        else if (operation == LiquidStakingOperation.Claim)
        {
            RequireTokenAccount(meta, accountIndexes[3], chain.Deployment.Coffee, chain.Deployment.VaultPda);
            RequireTokenAccount(meta, accountIndexes[4], chain.Deployment.Coffee, wallet);
        }
        else
        {
            RequireTokenAccount(meta, accountIndexes[2], chain.Deployment.Coffee, chain.Deployment.Admin);
            RequireTokenAccount(meta, accountIndexes[3], chain.Deployment.Coffee, chain.Deployment.VaultPda);
        }
    }

    private static List<SolanaEvent> DecodeEvents(JsonElement meta, string trustedProgram, LiquidStakingOperation operation)
    {
        var wanted = operation switch { LiquidStakingOperation.Deposit => SolanaAnchorEventCodec.Deposit, LiquidStakingOperation.Redeem => SolanaAnchorEventCodec.Redeem, LiquidStakingOperation.Claim => SolanaAnchorEventCodec.RewardClaimed, LiquidStakingOperation.RewardFunding => SolanaAnchorEventCodec.RewardFunded, _ => string.Empty };
        var result = new List<SolanaEvent>();
        foreach (var anchorEvent in SolanaAnchorEventCodec.Decode(meta.GetProperty("logMessages"), trustedProgram, wanted))
        {
            var payload = anchorEvent.Payload;
            if (anchorEvent.Name is SolanaAnchorEventCodec.Deposit or SolanaAnchorEventCodec.Redeem) result.Add(new SolanaEvent(anchorEvent.Name, ReadKey(payload, 0), ReadU64(payload, 32), ReadU64(payload, 40), 0, anchorEvent.LogIndex));
            else if (anchorEvent.Name == SolanaAnchorEventCodec.RewardClaimed) result.Add(new SolanaEvent(anchorEvent.Name, ReadKey(payload, 0), 0, 0, ReadU64(payload, 32), anchorEvent.LogIndex));
            else result.Add(new SolanaEvent(anchorEvent.Name, string.Empty, ReadU64(payload, 0), 0, ReadU64(payload, 0), anchorEvent.LogIndex));
        }
        return result;
    }

    private static string ReadKey(byte[] payload, int offset) => SolanaBase58.Encode(payload.AsSpan(offset, 32));
    private static ulong ReadU64(byte[] payload, int offset) => BitConverter.ToUInt64(payload, offset);
    private static BigInteger ToRaw(decimal amount, int decimals) => new(decimal.Truncate(amount * (decimal)BigInteger.Pow(10, decimals)));
    private static string? KeyString(JsonElement item) => item.ValueKind == JsonValueKind.String ? item.GetString() : item.TryGetProperty("pubkey", out var pubkey) ? pubkey.GetString() : null;
    private static bool PdaMatches(byte[] wallet, byte[] candidate, string seed, string program)
    {
        if (!SolanaBase58.TryDecode(program, out var programBytes) || candidate.Length != 32) return false;
        for (var bump = 255; bump >= 0; bump--)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Encoding.UTF8.GetBytes(seed)); hash.AppendData(wallet); hash.AppendData(new[] { (byte)bump }); hash.AppendData(programBytes); hash.AppendData(Encoding.UTF8.GetBytes("ProgramDerivedAddress"));
            if (hash.GetHashAndReset().SequenceEqual(candidate)) return true;
        }
        return false;
    }
    private static void RequireTokenAccount(JsonElement meta, int accountIndex, string mint, string owner)
    {
        var balances = meta.TryGetProperty("preTokenBalances", out var pre) ? pre.EnumerateArray() : Enumerable.Empty<JsonElement>();
        balances = balances.Concat(meta.TryGetProperty("postTokenBalances", out var post) ? post.EnumerateArray() : Enumerable.Empty<JsonElement>());
        if (!balances.Any(item => item.GetProperty("accountIndex").GetInt32() == accountIndex && item.GetProperty("mint").GetString() == mint && item.TryGetProperty("owner", out var accountOwner) && accountOwner.GetString() == owner)) throw new InvalidOperationException("The transaction did not prove the expected token-account owner and mint.");
    }
    private static decimal FromRaw(BigInteger amount, int decimals) => (decimal)amount / (decimal)BigInteger.Pow(10, decimals);
    private static decimal FromRaw(ulong amount, int decimals) => amount / (decimal)BigInteger.Pow(10, decimals);
    private static void RequireDecimals(ChainDefinition chain, VaultState vault, int cafeDecimals, int stCafeDecimals, int coffeeDecimals)
    {
        if (vault.CafeDecimals != cafeDecimals || vault.CafeDecimals != stCafeDecimals || vault.CoffeeDecimals != coffeeDecimals)
            throw new InvalidOperationException("The configured Solana mint decimals do not match the vault state.");
        if (chain.Deployment.CafeDecimals != cafeDecimals || chain.Deployment.StCafeDecimals != stCafeDecimals || chain.Deployment.CoffeeDecimals != coffeeDecimals)
            throw new InvalidOperationException("The configured Solana mint decimals do not match the deployment manifest.");
    }
    private sealed record VaultState(int CafeDecimals, int CoffeeDecimals, BigInteger RewardPerShare, ulong RewardRate, ulong PeriodFinish, ulong LastRewardSlot, ulong TotalShares, ulong CurrentSlot);
    private sealed record PositionState(ulong Shares, BigInteger RewardPerSharePaid, ulong PendingRewards);
    private static BigInteger ReadU128(byte[] bytes, int offset) => new(bytes.AsSpan(offset, 16), isUnsigned: true, isBigEndian: false);
    private static LiquidStakingDashboard Unavailable(string key, string wallet, string reason) => new() { ChainKey = key, Family = "Solana", WalletIdentifier = wallet, UnavailableReason = reason };
    private sealed record SolanaEvent(string Name, string Owner, ulong Assets, ulong Shares, ulong Reward, int Index);
}

internal static class SolanaBase58
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    public static bool IsPublicKey(string value) => TryDecode(value, out var bytes) && bytes.Length == 32;
    public static bool TryDecode(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>(); if (string.IsNullOrWhiteSpace(value)) return false;
        var digits = new byte[(value.Length * 733 / 1000) + 1]; var length = 1;
        foreach (var character in value) { var carry = Alphabet.IndexOf(character); if (carry < 0) return false; for (var i = 0; i < length; i++) { carry += 58 * digits[i]; digits[i] = (byte)(carry % 256); carry /= 256; } while (carry > 0) { digits[length++] = (byte)(carry % 256); carry /= 256; } }
        while (length > 0 && digits[length - 1] == 0) length--; var zeros = value.TakeWhile(c => c == '1').Count(); bytes = new byte[zeros + length]; for (var i = 0; i < length; i++) bytes[bytes.Length - 1 - i] = digits[i]; return true;
    }
    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        var digits = new byte[bytes.Length * 138 / 100 + 1]; var length = 1;
        foreach (var value in bytes) { var carry = (int)value; for (var i = 0; i < length; i++) { carry += 256 * digits[i]; digits[i] = (byte)(carry % 58); carry /= 58; } while (carry > 0) { digits[length++] = (byte)(carry % 58); carry /= 58; } }
        var result = new StringBuilder(bytes.Length * 2); for (var i = 0; i < bytes.Length && bytes[i] == 0; i++) result.Append('1'); for (var i = length - 1; i >= 0; i--) result.Append(Alphabet[digits[i]]); return result.ToString();
    }
}
