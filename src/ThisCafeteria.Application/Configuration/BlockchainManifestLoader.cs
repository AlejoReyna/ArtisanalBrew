using System.Globalization;
using System.Text.Json;

namespace ThisCafeteria.Application.Configuration;

public static class BlockchainManifestLoader
{
    public static BlockchainOptions LoadDeploymentManifests(BlockchainOptions options, string? evmManifestPath, string? solanaManifestPath)
    {
        var chains = options.Chains.ToList();
        // Keep the singular argument for compatibility, but accept a semicolon-separated
        // manifest list so one deployed application can expose multiple real EVM networks.
        foreach (var path in SplitManifestPaths(evmManifestPath))
        {
            if (TryReadEvm(path, out var evm)) Replace(chains, evm);
        }
        if (TryReadSolana(solanaManifestPath, out var solana)) Replace(chains, solana);
        return new BlockchainOptions { DefaultChainKey = options.DefaultChainKey, Chains = chains };
    }

    private static IEnumerable<string> SplitManifestPaths(string? value) =>
        (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
            var chainKey = root.GetProperty("chainKey").GetString() ?? string.Empty;
            var expectedChainKey = chainId switch
            {
                31337 => "evm-local",
                97 => "bsc-testnet",
                11155111 => "ethereum-sepolia",
                _ => throw new InvalidDataException($"Unsupported EVM manifest chain ID {chainId}.")
            };
            if (!string.Equals(chainKey, expectedChainKey, StringComparison.Ordinal))
                throw new InvalidDataException($"EVM chain ID {chainId} manifests must use chain key '{expectedChainKey}'.");
            var displayName = Optional(root, "displayName") ?? (chainId == 97 ? "BSC Testnet" : chainId == 11155111 ? "Ethereum Sepolia" : "Local EVM");
            var native = root.TryGetProperty("nativeCurrency", out var nativeElement) ? nativeElement : default;
            var nativeName = Optional(native, "name") ?? (chainId == 97 ? "BNB" : "Ether");
            var nativeSymbol = Optional(native, "symbol") ?? (chainId == 97 ? "tBNB" : "ETH");
            var nativeDecimals = native.ValueKind == JsonValueKind.Object && native.TryGetProperty("decimals", out var nativeDecimalsElement)
                ? nativeDecimalsElement.GetInt32()
                : 18;
            var rpcUrl = Optional(root, "rpcUrl") ?? (chainId == 97 ? "https://97.rpc.thirdweb.com" : chainId == 11155111 ? "https://ethereum-sepolia-rpc.publicnode.com" : "http://127.0.0.1:8545");
            var explorerAddress = Optional(root, "explorerAddressTemplate") ?? (chainId == 97 ? "https://testnet.bscscan.com/address/{0}" : chainId == 11155111 ? "https://sepolia.etherscan.io/address/{0}" : "http://127.0.0.1:8545/address/{0}");
            var explorerTransaction = Optional(root, "explorerTransactionTemplate") ?? (chainId == 97 ? "https://testnet.bscscan.com/tx/{0}" : chainId == 11155111 ? "https://sepolia.etherscan.io/tx/{0}" : "http://127.0.0.1:8545/tx/{0}");
            definition = new ChainDefinition
            {
                Key = chainKey,
                DisplayName = displayName,
                ShortName = displayName,
                Family = ChainFamily.Evm,
                Enabled = true,
                IconAsset = chainId == 97 ? "/images/bnb_logo.svg" : "/images/eth_logo.png",
                EvmChainId = chainId,
                EvmChainIdHex = $"0x{chainId:x}",
                NativeCurrencyName = nativeName,
                NativeCurrencySymbol = nativeSymbol,
                NativeCurrencyDecimals = nativeDecimals,
                PublicRpcUrl = rpcUrl,
                ExplorerAddressTemplate = explorerAddress,
                ExplorerTransactionTemplate = explorerTransaction,
                SortOrder = chainId == 97 ? 6 : chainId == 11155111 ? 1 : 100,
                Deployment = new ChainDeployment
                {
                    Cafe = addresses.GetProperty("cafe").GetString() ?? string.Empty,
                    Coffee = addresses.GetProperty("coffee").GetString() ?? string.Empty,
                    LiquidVault = addresses.GetProperty("liquidVault").GetString() ?? string.Empty,
                    Faucet = addresses.GetProperty("faucet").GetString() ?? string.Empty,
                    StCafe = addresses.GetProperty("liquidVault").GetString() ?? string.Empty,
                    AgenticEscrow = Optional(addresses, "erc8183Escrow") ?? string.Empty,
                    PaymentToken = Optional(addresses, "paymentToken") ?? (addresses.TryGetProperty("cafe", out var cafeElement) ? cafeElement.GetString() : string.Empty) ?? string.Empty,
                    EntryPoint = Optional(addresses, "entryPoint") ?? string.Empty,
                    AccountFactory = Optional(addresses, "accountFactory") ?? string.Empty,
                    VerifyingPaymaster = Optional(addresses, "verifyingPaymaster") ?? string.Empty,
                    ERC8004Registry = Optional(addresses, "erc8004Registry") ?? string.Empty,
                    ERC7683Resolver = Optional(addresses, "erc7683Resolver") ?? string.Empty,
                    ModularAccountFactory = Optional(addresses, "modularSimpleFactory") ?? string.Empty,
                    DelegationManager = Optional(addresses, "delegationManager") ?? string.Empty,
                    HybridDeleGatorImplementation = Optional(addresses, "hybridDeleGatorImplementation") ?? string.Empty,
                    AllowedTargetsEnforcer = Optional(addresses, "allowedTargetsEnforcer") ?? string.Empty,
                    AllowedMethodsEnforcer = Optional(addresses, "allowedMethodsEnforcer") ?? string.Empty,
                    ExactCalldataEnforcer = Optional(addresses, "exactCalldataEnforcer") ?? string.Empty,
                    LimitedCallsEnforcer = Optional(addresses, "limitedCallsEnforcer") ?? string.Empty,
                    NonceEnforcer = Optional(addresses, "nonceEnforcer") ?? string.Empty,
                    TimestampEnforcer = Optional(addresses, "timestampEnforcer") ?? string.Empty,
                    ModularAccountType = root.TryGetProperty("accountAbstraction", out var aa) ? Optional(aa, "modularAccountType") ?? string.Empty : string.Empty,
                    ModularFrameworkRevision = root.TryGetProperty("accountAbstraction", out var aa2) ? Optional(aa2, "modularFrameworkRevision") ?? string.Empty : string.Empty,
                    StartBlockOrSlot = ReadLong(root, "deployBlock")
                },
                Capabilities = new ChainCapabilities
                {
                    WalletLogin = true,
                    LiquidStaking = true,
                    Faucet = true,
                    RewardMinting = true,
                    AgenticCommerce = root.TryGetProperty("capabilities", out var caps) && caps.TryGetProperty("agenticCommerce", out var agenticCommerce) && agenticCommerce.GetBoolean(),
                    AgenticSessionPayments = root.TryGetProperty("capabilities", out var caps2) && caps2.TryGetProperty("agenticSessionPayments", out var agenticSessionPayments) && agenticSessionPayments.GetBoolean()
                }
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
            if (cluster is not ("localnet" or "devnet" or "testnet")) throw new InvalidDataException("Only Solana localnet, Devnet, and Testnet deployment manifests are supported.");
            var chainKey = Required(root, "chainKey");
            var expectedChainKey = $"solana-{cluster}";
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
                DisplayName = cluster switch { "localnet" => "Solana Localnet", "devnet" => "Solana Devnet", _ => "Solana Testnet" },
                ShortName = cluster switch { "localnet" => "Solana Localnet", "devnet" => "Solana Devnet", _ => "Solana Testnet" },
                Family = ChainFamily.Solana,
                Enabled = true,
                IconAsset = "/images/solana_logo.svg",
                SolanaCluster = cluster,
                NativeCurrencyName = "Solana",
                NativeCurrencySymbol = "SOL",
                NativeCurrencyDecimals = 9,
                PublicRpcUrl = Required(root, "rpcUrl"),
                ExplorerAddressTemplate = $"https://explorer.solana.com/address/{{0}}?cluster={(cluster == "localnet" ? "custom" : cluster)}",
                ExplorerTransactionTemplate = $"https://explorer.solana.com/tx/{{0}}?cluster={(cluster == "localnet" ? "custom" : cluster)}",
                SolanaCommitment = "finalized",
                SortOrder = cluster == "localnet" ? 101 : cluster == "devnet" ? 9 : 10,
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

    private static string? Optional(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long ReadLong(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var text) => text,
            _ => throw new InvalidDataException($"Manifest property '{name}' must be an integer.")
        };
    }

    private static bool TryOpen(string? path, out JsonDocument document)
    {
        document = null!;
        if (!TryResolveManifestPath(path, out var resolved)) return false;
        document = JsonDocument.Parse(File.ReadAllText(resolved));
        return true;
    }

    /// <summary>
    /// Resolves a manifest path so relative paths (e.g. "deployments/ethereum-sepolia.json" from
    /// appsettings) do not depend on the process working directory. A local `dotnet run --project`
    /// or IDE launch sets the CWD to the project directory, where repo-root "deployments/" does not
    /// exist; without this, the manifest silently fails to load and the registry falls back to the
    /// addressless default stub (surfacing as "liquid staking is not deployed on this chain yet").
    /// Absolute paths are used as-is; relative paths are probed against the CWD and each ancestor
    /// directory, mirroring how <c>LocalDotEnvLoader</c> locates the repo-root ".env".
    /// </summary>
    private static bool TryResolveManifestPath(string? path, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Path.IsPathRooted(path))
        {
            if (!File.Exists(path)) return false;
            resolved = path;
            return true;
        }

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, path);
            if (File.Exists(candidate))
            {
                resolved = candidate;
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }
}
