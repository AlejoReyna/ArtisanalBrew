namespace ThisCafeteria.Application.Configuration;

public interface IChainRegistry
{
    string DefaultChainKey { get; }
    IReadOnlyList<ChainDefinition> All { get; }
    bool TryGet(string key, out ChainDefinition definition);
    ChainDefinition GetRequired(string key);
}

public sealed class ChainRegistry : IChainRegistry
{
    private readonly IReadOnlyDictionary<string, ChainDefinition> _chains;
    public string DefaultChainKey { get; }
    public IReadOnlyList<ChainDefinition> All { get; }

    public ChainRegistry(BlockchainOptions options)
    {
        var chains = options.Chains.Count == 0 ? ChainDefinitionDefaults.PublicChains : options.Chains;
        Validate(options.DefaultChainKey, chains);
        All = chains.OrderBy(chain => chain.SortOrder).ToArray();
        _chains = All.ToDictionary(chain => chain.Key, StringComparer.Ordinal);
        DefaultChainKey = options.DefaultChainKey;
    }

    public bool TryGet(string key, out ChainDefinition definition) => _chains.TryGetValue(key, out definition!);

    public ChainDefinition GetRequired(string key) =>
        TryGet(key, out var definition) && definition.Enabled
            ? definition
            : throw new KeyNotFoundException($"Chain '{key}' is unknown or disabled.");

    private static void Validate(string defaultKey, IReadOnlyList<ChainDefinition> chains)
    {
        if (chains.Count == 0) throw new InvalidOperationException("At least one blockchain must be configured.");
        if (!chains.Any(chain => chain.Key == defaultKey)) throw new InvalidOperationException($"Default chain '{defaultKey}' is not configured.");
        if (chains.Any(chain => string.IsNullOrWhiteSpace(chain.Key) || chain.Key.Length > 64)) throw new InvalidOperationException("Chain keys must be non-empty and <= 64 characters.");
        if (chains.GroupBy(chain => chain.Key, StringComparer.Ordinal).Any(group => group.Count() != 1)) throw new InvalidOperationException("Chain keys must be unique.");
        var evmIds = chains.Where(chain => chain.Family == ChainFamily.Evm).Select(chain => chain.EvmChainId).Where(id => id.HasValue).ToList();
        if (evmIds.Count != evmIds.Distinct().Count()) throw new InvalidOperationException("EVM chain IDs must be unique.");
        foreach (var chain in chains)
        {
            if (!Uri.TryCreate(chain.PublicRpcUrl, UriKind.Absolute, out var publicRpc) || publicRpc.Scheme is not ("http" or "https")) throw new InvalidOperationException($"Chain '{chain.Key}' has an invalid public RPC URL.");
            if (!string.IsNullOrWhiteSpace(chain.ServerRpcUrl) && (!Uri.TryCreate(chain.ServerRpcUrl, UriKind.Absolute, out var serverRpc) || serverRpc.Scheme is not ("http" or "https"))) throw new InvalidOperationException($"Chain '{chain.Key}' has an invalid server RPC URL.");
            if (chain.Family == ChainFamily.Evm && (!chain.EvmChainId.HasValue || string.IsNullOrWhiteSpace(chain.EvmChainIdHex))) throw new InvalidOperationException($"EVM chain '{chain.Key}' must define chain ID and hex ID.");
            if (chain.Family == ChainFamily.Solana && string.IsNullOrWhiteSpace(chain.SolanaCluster)) throw new InvalidOperationException($"Solana chain '{chain.Key}' must define a cluster.");
            if (chain.Family == ChainFamily.Evm && chain.Capabilities.LiquidStaking && string.IsNullOrWhiteSpace(chain.Deployment.LiquidVault)) throw new InvalidOperationException($"Chain '{chain.Key}' enables liquid staking without a vault deployment.");
            if (chain.Capabilities.LegacyExit && string.IsNullOrWhiteSpace(chain.Deployment.LegacyPool)) throw new InvalidOperationException($"Chain '{chain.Key}' enables legacy exit without a pool deployment.");
            // Marketplace payment needs a settlement venue: the new liquid-chain rail settles through the
            // ERC-8183 agentic escrow, while the legacy Sepolia rail verifies a native transfer against its
            // legacy pool deployment. Either satisfies the "no capability without a contract behind it" rule;
            // a manifest that turns the flag on while carrying neither fails closed.
            if (chain.Capabilities.MarketplacePayment && chain.Family == ChainFamily.Evm && string.IsNullOrWhiteSpace(chain.Deployment.AgenticEscrow) && string.IsNullOrWhiteSpace(chain.Deployment.LegacyPool)) throw new InvalidOperationException($"Chain '{chain.Key}' enables marketplace payment without an escrow or legacy pool deployment.");
            if (chain.Family == ChainFamily.Solana && chain.Capabilities.LiquidStaking && string.IsNullOrWhiteSpace(chain.Deployment.Program)) throw new InvalidOperationException($"Solana chain '{chain.Key}' enables staking without a program deployment.");
            // A Solana CAFE faucet mints the CAFE token under its mint authority, so it needs both the
            // CAFE mint and the administrator authority that is allowed to mint. See docs/solana faucet.
            if (chain.Family == ChainFamily.Solana && chain.Capabilities.Faucet && (string.IsNullOrWhiteSpace(chain.Deployment.Cafe) || string.IsNullOrWhiteSpace(chain.Deployment.Admin))) throw new InvalidOperationException($"Solana chain '{chain.Key}' enables the faucet without a CAFE mint and administrator authority.");
            if (chain.Enabled && chain.Family == ChainFamily.Solana && chain.Capabilities.LiquidStaking)
            {
                var deploymentKeys = new[]
                {
                    chain.Deployment.Program, chain.Deployment.VaultPda, chain.Deployment.AuthorityPda,
                    chain.Deployment.Cafe, chain.Deployment.StCafe, chain.Deployment.Coffee,
                    chain.Deployment.CafeCustody, chain.Deployment.CoffeeCustody, chain.Deployment.Admin,
                    chain.Deployment.TokenProgram, chain.Deployment.Token2022Program
                };
                if (deploymentKeys.Any(key => !SolanaAddressRules.IsPublicKey(key))) throw new InvalidOperationException($"Enabled Solana chain '{chain.Key}' contains an invalid deployment public key.");
                if (chain.Deployment.TokenProgram != SolanaAddressRules.TokenProgram || chain.Deployment.Token2022Program != SolanaAddressRules.Token2022Program)
                    throw new InvalidOperationException($"Enabled Solana chain '{chain.Key}' must use the canonical SPL Token and Token-2022 programs.");
                if (chain.Deployment.CafeDecimals is < 0 or > 9 || chain.Deployment.StCafeDecimals != chain.Deployment.CafeDecimals || chain.Deployment.CoffeeDecimals is < 0 or > 9)
                    throw new InvalidOperationException($"Enabled Solana chain '{chain.Key}' contains unsupported token decimals.");
            }
        }
    }
}
