using System.Globalization;
using System.Text.Json;

namespace ThisCafeteria.Application.Configuration;

public static class BlockchainManifestLoader
{
    public static BlockchainOptions LoadDeploymentManifests(BlockchainOptions options, string? evmManifestPath, string? solanaManifestPath)
    {
        var chains = options.Chains.ToList();
        if (TryReadEvm(evmManifestPath, out var evm)) Replace(chains, evm);
        if (TryReadSolana(solanaManifestPath, out var solana)) Replace(chains, solana);
        return new BlockchainOptions { DefaultChainKey = options.DefaultChainKey, Chains = chains };
    }

    private static void Replace(List<ChainDefinition> chains, ChainDefinition definition)
    {
        chains.RemoveAll(chain => string.Equals(chain.Key, definition.Key, StringComparison.Ordinal));
        chains.Add(definition);
    }

    private static bool TryReadEvm(string? path, out ChainDefinition definition)
    {
        definition = new ChainDefinition();
        if (!TryOpen(path, out var document)) return false;
        using (document)
        {
            var root = document.RootElement;
            var addresses = root.GetProperty("addresses");
            var chainId = root.GetProperty("chainId").GetInt32();
            definition = new ChainDefinition
            {
                Key = root.GetProperty("chainKey").GetString() ?? "evm-local",
                DisplayName = "Local EVM",
                ShortName = "Local EVM",
                Family = ChainFamily.Evm,
                EvmChainId = chainId,
                EvmChainIdHex = $"0x{chainId:x}",
                NativeCurrencyName = "Local Ether",
                NativeCurrencySymbol = "ETH",
                PublicRpcUrl = "http://127.0.0.1:8545",
                ExplorerAddressTemplate = "http://127.0.0.1:8545/address/{0}",
                ExplorerTransactionTemplate = "http://127.0.0.1:8545/tx/{0}",
                SortOrder = 100,
                Deployment = new ChainDeployment
                {
                    Cafe = addresses.GetProperty("cafe").GetString() ?? string.Empty,
                    Coffee = addresses.GetProperty("coffee").GetString() ?? string.Empty,
                    LiquidVault = addresses.GetProperty("liquidVault").GetString() ?? string.Empty,
                    Faucet = addresses.GetProperty("faucet").GetString() ?? string.Empty,
                    StCafe = addresses.GetProperty("liquidVault").GetString() ?? string.Empty,
                    StartBlockOrSlot = long.Parse(root.GetProperty("deployBlock").GetString() ?? "0", CultureInfo.InvariantCulture)
                },
                Capabilities = new ChainCapabilities { WalletLogin = true, LiquidStaking = true, Faucet = true, RewardMinting = true }
            };
            return true;
        }
    }

    private static bool TryReadSolana(string? path, out ChainDefinition definition)
    {
        definition = new ChainDefinition();
        if (!TryOpen(path, out var document)) return false;
        using (document)
        {
            var root = document.RootElement;
            if (!string.Equals(root.GetProperty("schemaVersion").GetString(), "1", StringComparison.Ordinal)) throw new InvalidDataException("Unsupported Solana manifest schema version.");
            var cluster = Required(root, "cluster");
            if (cluster is not ("localnet" or "testnet")) throw new InvalidDataException("Only Solana localnet and Testnet deployment manifests are supported.");
            var chainKey = Required(root, "chainKey");
            var expectedChainKey = cluster == "localnet" ? "solana-localnet" : "solana-testnet";
            if (!string.Equals(chainKey, expectedChainKey, StringComparison.Ordinal)) throw new InvalidDataException($"Solana {cluster} manifests must use chain key '{expectedChainKey}'.");
            var statePda = Required(root, "statePda");
            var authorityPda = Required(root, "authorityPda");
            if (!string.Equals(statePda, authorityPda, StringComparison.Ordinal)) throw new InvalidDataException("The current program requires the vault state PDA to also be the token authority PDA.");
            var cafeDecimals = root.GetProperty("cafeDecimals").GetInt32();
            var stCafeDecimals = root.GetProperty("stCafeDecimals").GetInt32();
            var coffeeDecimals = root.GetProperty("coffeeDecimals").GetInt32();
            if (cafeDecimals is < 0 or > 9 || stCafeDecimals != cafeDecimals || coffeeDecimals is < 0 or > 9) throw new InvalidDataException("The Solana manifest contains unsupported token decimals.");
            definition = new ChainDefinition
            {
                Key = chainKey,
                DisplayName = cluster == "localnet" ? "Solana Localnet" : "Solana Testnet",
                ShortName = cluster == "localnet" ? "Solana Localnet" : "Solana Testnet",
                Family = ChainFamily.Solana,
                Enabled = true,
                SolanaCluster = cluster,
                NativeCurrencyName = "Solana",
                NativeCurrencySymbol = "SOL",
                NativeCurrencyDecimals = 9,
                PublicRpcUrl = Required(root, "rpcUrl"),
                ExplorerAddressTemplate = $"https://explorer.solana.com/address/{{0}}?cluster={(cluster == "localnet" ? "custom" : "testnet")}",
                ExplorerTransactionTemplate = $"https://explorer.solana.com/tx/{{0}}?cluster={(cluster == "localnet" ? "custom" : "testnet")}",
                SolanaCommitment = "finalized",
                SortOrder = cluster == "localnet" ? 101 : 9,
                Deployment = new ChainDeployment
                {
                    Program = Required(root, "programId"),
                    VaultPda = statePda,
                    AuthorityPda = authorityPda,
                    Cafe = Required(root, "cafeMint"),
                    StCafe = Required(root, "stCafeMint"),
                    Coffee = Required(root, "coffeeMint"),
                    CafeCustody = Required(root, "cafeCustody"),
                    CoffeeCustody = Required(root, "coffeeCustody"),
                    Admin = Required(root, "administrator"),
                    TokenProgram = Required(root, "tokenProgram"),
                    Token2022Program = Required(root, "token2022Program"),
                    CafeDecimals = cafeDecimals,
                    StCafeDecimals = stCafeDecimals,
                    CoffeeDecimals = coffeeDecimals,
                    StartBlockOrSlot = root.GetProperty("deploymentSlot").GetInt64()
                },
                Capabilities = new ChainCapabilities { WalletLogin = true, LiquidStaking = true, RewardMinting = true }
            };
            return true;
        }
    }

    private static string Required(JsonElement root, string name)
    {
        var value = root.GetProperty(name).GetString();
        return !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException($"Manifest property '{name}' is required.");
    }

    private static bool TryOpen(string? path, out JsonDocument document)
    {
        document = null!;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        document = JsonDocument.Parse(File.ReadAllText(path));
        return true;
    }
}
