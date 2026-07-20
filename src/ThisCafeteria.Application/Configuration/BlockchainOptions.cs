namespace ThisCafeteria.Application.Configuration;

public sealed class BlockchainOptions
{
    public const string SectionName = "Blockchain";
    public string DefaultChainKey { get; init; } = "ethereum-sepolia";
    public IReadOnlyList<ChainDefinition> Chains { get; init; } = Array.Empty<ChainDefinition>();

    public static BlockchainOptions CreateDefaults() => new()
    {
        DefaultChainKey = "ethereum-sepolia",
        Chains = ChainDefinitionDefaults.PublicChains
    };
}

public static class ChainDefinitionDefaults
{
    public static IReadOnlyList<ChainDefinition> PublicChains { get; } =
    [
        Evm("ethereum-sepolia", "Ethereum Sepolia", "ETH Sepolia", 11155111, "ETH", "https://ethereum-sepolia-rpc.publicnode.com", "https://sepolia.etherscan.io", 1, legacy: true),
        Evm("hedera-testnet", "Hedera Testnet", "Hedera", 296, "HBAR", "https://296.rpc.thirdweb.com", "https://hashscan.io/testnet", 2, enabled: false),
        Evm("avalanche-fuji", "Avalanche Fuji", "Fuji", 43113, "AVAX", "https://43113.rpc.thirdweb.com", "https://testnet.snowtrace.io", 3, enabled: false),
        Evm("linea-sepolia", "Linea Sepolia", "Linea", 59141, "ETH", "https://59141.rpc.thirdweb.com", "https://sepolia.lineascan.build", 4, enabled: false),
        Evm("base-sepolia", "Base Sepolia", "Base", 84532, "ETH", "https://84532.rpc.thirdweb.com", "https://sepolia.basescan.org", 5, enabled: false),
        Evm("bsc-testnet", "BNB Smart Chain Testnet", "BSC Testnet", 97, "tBNB", "https://97.rpc.thirdweb.com", "https://testnet.bscscan.com", 6, enabled: false, iconAsset: "/images/bnb_logo.svg"),
        Evm("monad-testnet", "Monad Testnet", "Monad", 10143, "MON", "https://10143.rpc.thirdweb.com", "https://monad-testnet.socialscan.io", 7, enabled: false),
        Evm("arbitrum-sepolia", "Arbitrum Sepolia", "Arbitrum", 421614, "ETH", "https://421614.rpc.thirdweb.com", "https://sepolia.arbiscan.io", 8, enabled: false),
        new ChainDefinition
        {
            Key = "solana-testnet", DisplayName = "Solana Testnet", ShortName = "Solana Testnet", Family = ChainFamily.Solana,
            Enabled = false,
            IconAsset = "/images/solana_logo.svg", SolanaCluster = "testnet", NativeCurrencyName = "Solana", NativeCurrencySymbol = "SOL",
            NativeCurrencyDecimals = 9, PublicRpcUrl = "https://api.testnet.solana.com", ExplorerAddressTemplate = "https://explorer.solana.com/address/{0}?cluster=testnet",
            ExplorerTransactionTemplate = "https://explorer.solana.com/tx/{0}?cluster=testnet", SortOrder = 9,
            // A verified deployment manifest replaces and enables this entry.
            // Keeping the undeployed definition disabled prevents either selector
            // and the public chain API from advertising a non-working connection.
            Capabilities = new ChainCapabilities()
        }
    ];

    private static ChainDefinition Evm(string key, string name, string shortName, int chainId, string symbol, string rpc, string explorer, int order, bool legacy = false, bool enabled = true, string iconAsset = "/images/eth_logo.png") => new()
    {
        Key = key,
        DisplayName = name,
        ShortName = shortName,
        Family = ChainFamily.Evm,
        EvmChainId = chainId,
        EvmChainIdHex = $"0x{chainId:x}",
        NativeCurrencyName = symbol,
        NativeCurrencySymbol = symbol,
        PublicRpcUrl = rpc,
        ExplorerAddressTemplate = $"{explorer.TrimEnd('/')}/address/{{0}}",
        ExplorerTransactionTemplate = $"{explorer.TrimEnd('/')}/tx/{{0}}",
        SortOrder = order,
        Enabled = enabled,
        IconAsset = iconAsset,
        Deployment = legacy ? new ChainDeployment
        {
            Cafe = "0x15DbED39271D2788a9Be63ffB34C8E2DdED8754A",
            Coffee = "0x4056E7F5FD1584C3db6223c9483761Dcb30Bf21C",
            LegacyPool = "0x932d50E20F917B9BbBe2C40F30D43BCef0e93F90",
            Faucet = "0xBD1517529BB0BA20c43b4E39323C70058FADe86D"
        } : new(),
        Capabilities = new ChainCapabilities { WalletLogin = true, LegacyExit = legacy, Faucet = legacy, MarketplacePayment = legacy, RewardMinting = legacy }
    };
}
