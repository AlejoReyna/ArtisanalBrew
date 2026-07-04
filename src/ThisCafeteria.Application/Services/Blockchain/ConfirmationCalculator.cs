using System.Numerics;

namespace ThisCafeteria.Application.Services.Blockchain;

public static class ConfirmationCalculator
{
    public static int Calculate(BigInteger currentBlock, BigInteger? receiptBlock)
    {
        if (receiptBlock is null)
        {
            return 0;
        }

        var confirmations = currentBlock - receiptBlock.Value + 1;
        return confirmations < 0 ? 0 : (int)confirmations;
    }
}
