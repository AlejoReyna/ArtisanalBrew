namespace ThisCafeteria.Application.Services.Blockchain;

public static class StakingAmountRules
{
    public static bool IsValidStakeAmount(decimal amount) => amount > 0m;

    public static bool IsValidUnstakeAmount(decimal amount, decimal stakedBalance) =>
        amount > 0m &&
        stakedBalance > 0m &&
        amount <= stakedBalance;
}
