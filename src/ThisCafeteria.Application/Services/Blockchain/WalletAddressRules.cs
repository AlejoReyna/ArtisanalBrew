using Nethereum.Util;

namespace ThisCafeteria.Application.Services.Blockchain;

public static class WalletAddressRules
{
    public static bool TryNormalizeWallet(string? address, out string checksum)
    {
        checksum = string.Empty;
        if (string.IsNullOrWhiteSpace(address) ||
            !AddressUtil.Current.IsValidEthereumAddressHexFormat(address))
        {
            return false;
        }

        checksum = AddressUtil.Current.ConvertToChecksumAddress(address);
        return true;
    }

    public static bool IsConfiguredAddress(string? address) =>
        !string.IsNullOrWhiteSpace(address) &&
        address.Length == 42 &&
        address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
        address[2..].All(Uri.IsHexDigit) &&
        !address.Equals("0x0000000000000000000000000000000000000000", StringComparison.OrdinalIgnoreCase);

    public static bool TryNormalizeTransactionHash(string? value, out string transactionHash)
    {
        transactionHash = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != 66 ||
            !value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            !value[2..].All(Uri.IsHexDigit))
        {
            return false;
        }

        transactionHash = value.ToLowerInvariant();
        return true;
    }
}
