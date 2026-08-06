using Microsoft.Extensions.Logging;
namespace ThisCafeteria.Infrastructure.Services.Blockchain;

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

    public const string CafeFaucet =
        "[" +
        "{'constant':false,'inputs':[],'name':'claim','outputs':[],'type':'function'}," +
        "{'constant':true,'inputs':[],'name':'claimAmount','outputs':[{'name':'','type':'uint256'}],'type':'function'}," +
        "{'constant':true,'inputs':[],'name':'cooldownSeconds','outputs':[{'name':'','type':'uint256'}],'type':'function'}," +
        "{'constant':true,'inputs':[{'name':'account','type':'address'}],'name':'nextClaimAt','outputs':[{'name':'','type':'uint256'}],'type':'function'}," +
        "{'constant':true,'inputs':[{'name':'account','type':'address'}],'name':'canClaim','outputs':[{'name':'','type':'bool'}],'type':'function'}" +
        "]";

    public const string StakingPool =
        "[" +
        "{'constant':true,'inputs':[{'name':'account','type':'address'}],'name':'balanceOf','outputs':[{'name':'','type':'uint256'}],'type':'function'}," +
        "{'constant':true,'inputs':[{'name':'account','type':'address'}],'name':'earned','outputs':[{'name':'','type':'uint256'}],'type':'function'}," +
        "{'constant':false,'inputs':[{'name':'amount','type':'uint256'}],'name':'stake','outputs':[],'type':'function'}," +
        "{'constant':false,'inputs':[{'name':'amount','type':'uint256'}],'name':'unstake','outputs':[],'type':'function'}," +
        "{'constant':false,'inputs':[{'name':'amount','type':'uint256'}],'name':'withdraw','outputs':[],'type':'function'}," +
        "{'constant':false,'inputs':[],'name':'getReward','outputs':[],'type':'function'}," +
        "{'constant':false,'inputs':[],'name':'claimReward','outputs':[],'type':'function'}," +
        "{'constant':true,'inputs':[],'name':'rewardRate','outputs':[{'name':'','type':'uint256'}],'type':'function'}," +
        "{'constant':true,'inputs':[],'name':'aprBasisPoints','outputs':[{'name':'','type':'uint256'}],'type':'function'}," +
        "{'constant':true,'inputs':[],'name':'totalSupply','outputs':[{'name':'','type':'uint256'}],'type':'function'}" +
        "]";

    public const string LiquidVault = "[" +
        "{'inputs':[{'name':'assets','type':'uint256'},{'name':'receiver','type':'address'}],'name':'deposit','outputs':[{'name':'shares','type':'uint256'}],'stateMutability':'nonpayable','type':'function'}," +
        "{'inputs':[{'name':'shares','type':'uint256'},{'name':'receiver','type':'address'},{'name':'owner','type':'address'}],'name':'redeem','outputs':[{'name':'assets','type':'uint256'}],'stateMutability':'nonpayable','type':'function'}," +
        "{'inputs':[{'name':'assets','type':'uint256'}],'name':'previewDeposit','outputs':[{'name':'shares','type':'uint256'}],'stateMutability':'view','type':'function'}," +
        "{'inputs':[{'name':'shares','type':'uint256'}],'name':'previewRedeem','outputs':[{'name':'assets','type':'uint256'}],'stateMutability':'view','type':'function'}," +
        "{'inputs':[{'name':'account','type':'address'}],'name':'earned','outputs':[{'name':'','type':'uint256'}],'stateMutability':'view','type':'function'}," +
        "{'inputs':[],'name':'totalAssets','outputs':[{'name':'','type':'uint256'}],'stateMutability':'view','type':'function'}," +
        "{'inputs':[],'name':'totalSupply','outputs':[{'name':'','type':'uint256'}],'stateMutability':'view','type':'function'}," +
        "{'inputs':[],'name':'rewardRate','outputs':[{'name':'','type':'uint256'}],'stateMutability':'view','type':'function'}," +
        "{'inputs':[],'name':'periodFinish','outputs':[{'name':'','type':'uint256'}],'stateMutability':'view','type':'function'}," +
        "{'inputs':[],'name':'claimRewards','outputs':[{'name':'reward','type':'uint256'}],'stateMutability':'nonpayable','type':'function'}" +
        "]";
}
