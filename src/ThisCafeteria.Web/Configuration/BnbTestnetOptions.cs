namespace ThisCafeteria.Web.Configuration;

public sealed class BnbTestnetOptions
{
    public const string SectionName = "BnbTestnet";

    public string NetworkName { get; init; } = "BSC Testnet";
    public int ChainId { get; init; } = 97;
    public string ChainIdHex { get; init; } = "0x61";
    public string RpcUrl { get; init; } = "https://data-seed-prebsc-1-s1.bnbchain.org:8545";
    public string CurrencyName { get; init; } = "Test BNB";
    public string CurrencySymbol { get; init; } = "tBNB";
    public int CurrencyDecimals { get; init; } = 18;
    public string ExplorerUrl { get; init; } = "https://testnet.bscscan.com/";
}
