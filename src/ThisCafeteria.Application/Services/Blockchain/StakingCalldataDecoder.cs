using System.Globalization;
using System.Numerics;
using Nethereum.Util;

namespace ThisCafeteria.Application.Services.Blockchain;

public static class StakingCalldataDecoder
{
    public static readonly string[] ClaimFunctionNames = ["getReward", "claimReward"];
    public static readonly string[] ClaimFunctionSignatures = ["getReward()", "claimReward()"];

    private const string Erc20TransferSelector = "0xa9059cbb";

    public static string FunctionSelector(string signature)
    {
        var hash = Sha3Keccack.Current.CalculateHash(signature);
        return $"0x{hash[..8]}";
    }

    public static bool TryDecodeErc20Transfer(
        string? input,
        out string recipient,
        out BigInteger amount)
    {
        recipient = string.Empty;
        amount = BigInteger.Zero;

        if (string.IsNullOrWhiteSpace(input) ||
            !input.StartsWith(Erc20TransferSelector, StringComparison.OrdinalIgnoreCase) ||
            input.Length < 138)
        {
            return false;
        }

        recipient = $"0x{input.Substring(10 + 24, 40)}";
        amount = BigInteger.Parse($"0{input.Substring(10 + 64, 64)}", NumberStyles.HexNumber);
        return AddressUtil.Current.IsValidEthereumAddressHexFormat(recipient);
    }

    public static bool TryDecodeStakingAmount(
        string? input,
        StakingTransactionType transactionType,
        out BigInteger amount)
    {
        amount = BigInteger.Zero;

        if (string.IsNullOrWhiteSpace(input) || input.Length < 74)
        {
            return false;
        }

        var stakeSelector = FunctionSelector("stake(uint256)");
        var unstakeSelector = FunctionSelector("unstake(uint256)");
        var withdrawSelector = FunctionSelector("withdraw(uint256)");
        var selectorMatches = transactionType switch
        {
            StakingTransactionType.Stake => input.StartsWith(stakeSelector, StringComparison.OrdinalIgnoreCase),
            StakingTransactionType.Unstake =>
                input.StartsWith(unstakeSelector, StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith(withdrawSelector, StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        if (!selectorMatches)
        {
            return false;
        }

        amount = BigInteger.Parse($"0{input.Substring(10, 64)}", NumberStyles.HexNumber);
        return true;
    }

    public static bool TryDecodeClaimSelector(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length < 10)
        {
            return false;
        }

        return ClaimFunctionSignatures
            .Select(FunctionSelector)
            .Any(selector => input.StartsWith(selector, StringComparison.OrdinalIgnoreCase));
    }
}
