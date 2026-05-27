namespace ThisCafeteria.Web.Services.Blockchain;

internal static class ContractAbis
{
    public const string Erc20BalanceOf =
        "[{'constant':true,'inputs':[{'name':'owner','type':'address'}],'name':'balanceOf','outputs':[{'name':'','type':'uint256'}],'type':'function'}]";

    public const string Erc20Transfer =
        "[{'constant':false,'inputs':[{'name':'recipient','type':'address'},{'name':'amount','type':'uint256'}],'name':'transfer','outputs':[{'name':'','type':'bool'}],'type':'function'}]";

    public const string Erc20Approve =
        "[{'constant':false,'inputs':[{'name':'spender','type':'address'},{'name':'amount','type':'uint256'}],'name':'approve','outputs':[{'name':'','type':'bool'}],'type':'function'}]";

    public const string CoffeeCoinMint =
        "[{'inputs':[{'name':'to','type':'address'},{'name':'amount','type':'uint256'}],'name':'mint','outputs':[],'stateMutability':'nonpayable','type':'function'}]";

    public const string Erc20TotalSupply =
        "[{'constant':true,'inputs':[],'name':'totalSupply','outputs':[{'name':'','type':'uint256'}],'type':'function'}]";

    public const string StakingPool =
        "[" +
        "{'constant':true,'inputs':[{'name':'account','type':'address'}],'name':'balanceOf','outputs':[{'name':'','type':'uint256'}],'type':'function'}," +
        "{'constant':true,'inputs':[{'name':'account','type':'address'}],'name':'earned','outputs':[{'name':'','type':'uint256'}],'type':'function'}," +
        "{'constant':false,'inputs':[{'name':'amount','type':'uint256'}],'name':'stake','outputs':[],'type':'function'}," +
        "{'constant':false,'inputs':[{'name':'amount','type':'uint256'}],'name':'unstake','outputs':[],'type':'function'}," +
        "{'constant':false,'inputs':[{'name':'amount','type':'uint256'}],'name':'withdraw','outputs':[],'type':'function'}" +
        "]";
}
