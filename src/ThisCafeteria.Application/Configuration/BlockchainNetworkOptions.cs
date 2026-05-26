namespace ThisCafeteria.Application.Configuration;

public sealed class BlockchainNetworkOptions
{
    public const string SectionName = "Blockchain:Network";
    public const string LegacySectionName = "Blockchain:BNBTestnet";

    public string NetworkName { get; init; } = "Ethereum Sepolia";
    public int ChainId { get; init; } = 11155111;
    public string ChainIdHex { get; init; } = "0xaa36a7";
    public string RpcUrl { get; init; } = "https://ethereum-sepolia-rpc.publicnode.com";
    public string CurrencyName { get; init; } = "Sepolia ETH";
    public string CurrencySymbol { get; init; } = "ETH";
    public int CurrencyDecimals { get; init; } = 18;
    public string ExplorerUrl { get; init; } = "https://sepolia.etherscan.io/";

    /// <summary>
    /// ERC-20 token used for coffee payments on the configured network.
    /// Deploy and configure a Sepolia token address before enabling token payments.
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
