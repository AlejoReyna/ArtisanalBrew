namespace ThisCafeteria.Web.Services.Blockchain;

internal static class ContractAbis
{
    public const string Erc20BalanceOf =
        "[{'constant':true,'inputs':[{'name':'owner','type':'address'}],'name':'balanceOf','outputs':[{'name':'','type':'uint256'}],'type':'function'}]";

    public const string Erc20Transfer =
        "[{'constant':false,'inputs':[{'name':'recipient','type':'address'},{'name':'amount','type':'uint256'}],'name':'transfer','outputs':[{'name':'','type':'bool'}],'type':'function'}]";

    public const string CoffeeCoinMint =
        "[{'inputs':[{'name':'to','type':'address'},{'name':'amount','type':'uint256'}],'name':'mint','outputs':[],'stateMutability':'nonpayable','type':'function'}]";

    public const string Erc20TotalSupply =
        "[{'constant':true,'inputs':[],'name':'totalSupply','outputs':[{'name':'','type':'uint256'}],'type':'function'}]";
}
