namespace ThisCafeteria.Web.Configuration;

public sealed class BnbTestnetOptions
{
    public const string SectionName = "Blockchain:BNBTestnet";

    public string NetworkName { get; init; } = "BSC Testnet";
    public int ChainId { get; init; } = 97;
    public string ChainIdHex { get; init; } = "0x61";
    public string RpcUrl { get; init; } = "https://rpc.ankr.com/bsc_testnet_chapel/56e119a6270f4441ea452c1756c15ec402eb41bcb0965b5cb4b0fec0a6b4cb51";
    public string CurrencyName { get; init; } = "Test BNB";
    public string CurrencySymbol { get; init; } = "tBNB";
    public int CurrencyDecimals { get; init; } = 18;
    public string ExplorerUrl { get; init; } = "https://testnet.bscscan.com/";
    public string AnkrBNBContract { get; init; } = string.Empty;
    public string StakingPoolContract { get; init; } = string.Empty;
    public string CoffeeCoinContract { get; init; } = string.Empty;
    public string MarketplaceWallet { get; init; } = string.Empty;
    public decimal StakingAprPercent { get; init; } = 5.2m;
}
