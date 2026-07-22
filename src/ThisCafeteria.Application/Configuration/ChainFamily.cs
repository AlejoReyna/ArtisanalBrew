namespace ThisCafeteria.Application.Configuration;

public enum ChainFamily
{
    Evm,
    Solana
}

public sealed record ChainCapabilities
{
    public bool WalletLogin { get; init; }
    public bool LiquidStaking { get; init; }
    public bool LegacyExit { get; init; }
    public bool Faucet { get; init; }
    public bool MarketplacePayment { get; init; }
    public bool RewardMinting { get; init; }
    public bool AgenticCommerce { get; init; }

    /// <summary>
    /// True when the chain has a verified MetaMask Delegation Framework deployment (see
    /// <see cref="ChainDeployment.ModularAccountFactory"/> and friends) so agent session-key
    /// permissions can be installed on modular accounts. See docs/erc4337-session-key-provenance.md.
    /// </summary>
    public bool AgenticSessionPayments { get; init; }
}

public sealed record ChainDeployment
{
    public string Cafe { get; init; } = string.Empty;
    public string Coffee { get; init; } = string.Empty;
    public string StCafe { get; init; } = string.Empty;
    public string LiquidVault { get; init; } = string.Empty;
    public string LegacyPool { get; init; } = string.Empty;
    public string Faucet { get; init; } = string.Empty;
    public string AgenticEscrow { get; init; } = string.Empty;
    public string PaymentToken { get; init; } = string.Empty;
    public string EntryPoint { get; init; } = string.Empty;
    public string AccountFactory { get; init; } = string.Empty;
    public string VerifyingPaymaster { get; init; } = string.Empty;
    public string ERC8004Registry { get; init; } = string.Empty;
    public string ERC7683Resolver { get; init; } = string.Empty;

    // --- Modular account stack (MetaMask Delegation Framework v1.3.0, commit bfbdf9795a976833ed2fa000baf42fbb83958b03).
    // See docs/erc4337-session-key-provenance.md for the provenance matrix. Every field here must
    // point at unmodified, audited contract deployments — SmartAccountService fails closed unless
    // all of them (plus EntryPoint) are present.
    public string ModularAccountFactory { get; init; } = string.Empty;
    public string DelegationManager { get; init; } = string.Empty;
    public string HybridDeleGatorImplementation { get; init; } = string.Empty;
    public string AllowedTargetsEnforcer { get; init; } = string.Empty;
    public string AllowedMethodsEnforcer { get; init; } = string.Empty;
    public string ExactCalldataEnforcer { get; init; } = string.Empty;
    public string LimitedCallsEnforcer { get; init; } = string.Empty;
    public string NonceEnforcer { get; init; } = string.Empty;
    public string TimestampEnforcer { get; init; } = string.Empty;
    public string ModularAccountType { get; init; } = string.Empty;
    public string ModularFrameworkRevision { get; init; } = string.Empty;

    public string Program { get; init; } = string.Empty;
    public string VaultPda { get; init; } = string.Empty;
    public string AuthorityPda { get; init; } = string.Empty;
    public string CafeCustody { get; init; } = string.Empty;
    public string CoffeeCustody { get; init; } = string.Empty;
    public string Admin { get; init; } = string.Empty;
    public string TokenProgram { get; init; } = string.Empty;
    public string Token2022Program { get; init; } = string.Empty;
    public int CafeDecimals { get; init; } = 18;
    public int StCafeDecimals { get; init; } = 18;
    public int CoffeeDecimals { get; init; } = 18;
    public long StartBlockOrSlot { get; init; }
}

public sealed record ChainDefinition
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ShortName { get; init; } = string.Empty;
    public string IconAsset { get; init; } = "/images/eth_logo.png";
    public ChainFamily Family { get; init; }
    public bool Enabled { get; init; } = true;
    public int SortOrder { get; init; }
    public int? EvmChainId { get; init; }
    public string? EvmChainIdHex { get; init; }
    public string? SolanaCluster { get; init; }
    public string NativeCurrencyName { get; init; } = string.Empty;
    public string NativeCurrencySymbol { get; init; } = string.Empty;
    public int NativeCurrencyDecimals { get; init; } = 18;
    public string PublicRpcUrl { get; init; } = string.Empty;
    public string? ServerRpcUrl { get; init; }
    public string ExplorerAddressTemplate { get; init; } = string.Empty;
    public string ExplorerTransactionTemplate { get; init; } = string.Empty;
    public int MinimumConfirmations { get; init; } = 2;
    public string SolanaCommitment { get; init; } = "confirmed";
    public ChainDeployment Deployment { get; init; } = new();
    public ChainCapabilities Capabilities { get; init; } = new();

    public string EffectiveServerRpcUrl => string.IsNullOrWhiteSpace(ServerRpcUrl) ? PublicRpcUrl : ServerRpcUrl;
    public string FamilyName => Family.ToString();
}
