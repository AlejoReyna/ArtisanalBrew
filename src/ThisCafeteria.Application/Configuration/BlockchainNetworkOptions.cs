namespace ThisCafeteria.Application.Configuration;

public sealed class BlockchainNetworkOptions
{
    public const string SectionName = "Blockchain:Network";
    public const string LegacySectionName = "Blockchain:BNBTestnet";

    public string NetworkName { get; init; } = "Base Sepolia";
    public int ChainId { get; init; } = 84532;
    public string ChainIdHex { get; init; } = "0x14a34";
    public string RpcUrl { get; init; } = "https://sepolia.base.org";
    public string CurrencyName { get; init; } = "Sepolia ETH";
    public string CurrencySymbol { get; init; } = "ETH";
    public int CurrencyDecimals { get; init; } = 18;
    public string ExplorerUrl { get; init; } = "https://sepolia-explorer.base.org/";

    /// <summary>
    /// ERC-20 token used for coffee payments on the configured network.
    /// Deploy and configure a Base Sepolia token address before enabling token payments.
    /// </summary>
    public string PaymentTokenContract { get; init; } = string.Empty;

    public string AnkrBNBContract { get; init; } = string.Empty;
    public string StakingPoolContract { get; init; } = string.Empty;
    public string CoffeeCoinContract { get; init; } = string.Empty;
    public string MarketplaceWallet { get; init; } = string.Empty;
    public decimal StakingAprPercent { get; init; } = 5.2m;

    public string EffectivePaymentTokenContract =>
        !string.IsNullOrWhiteSpace(PaymentTokenContract)
            ? PaymentTokenContract
            : AnkrBNBContract;
}
