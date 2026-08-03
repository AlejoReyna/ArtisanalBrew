namespace ThisCafeteria.Application.Services.Blockchain;

/// <summary>
/// Verifies a native-currency checkout payment to the selected chain's configured marketplace
/// destination (<see cref="Configuration.ChainDeployment.LegacyPool"/>), the same settlement venue
/// <see cref="Configuration.ChainCapabilities.MarketplacePayment"/> validates against.
/// </summary>
public interface IMarketplacePaymentGateway
{
    Task<StakingVerificationResult> VerifyNativePaymentAsync(
        string chainKey,
        string transactionHash,
        string expectedFromWallet,
        decimal expectedAmount,
        CancellationToken cancellationToken = default);
}
