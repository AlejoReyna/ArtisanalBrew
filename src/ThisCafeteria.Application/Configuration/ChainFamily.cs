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
}

public sealed record ChainDeployment
{
    public string Cafe { get; init; } = string.Empty;
    public string Coffee { get; init; } = string.Empty;
    public string StCafe { get; init; } = string.Empty;
    public string LiquidVault { get; init; } = string.Empty;
    public string LegacyPool { get; init; } = string.Empty;
    public string Faucet { get; init; } = string.Empty;
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
