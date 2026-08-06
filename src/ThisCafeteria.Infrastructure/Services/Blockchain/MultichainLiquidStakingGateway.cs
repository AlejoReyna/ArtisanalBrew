using Microsoft.Extensions.Logging;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services.Blockchain;

namespace ThisCafeteria.Infrastructure.Services.Blockchain;

public sealed class MultichainLiquidStakingGateway(
    IChainRegistry registry,
    EvmLiquidStakingGateway evm,
    SolanaLiquidStakingGateway solana) : ILiquidStakingGateway
{
    public Task<LiquidStakingDashboard> GetDashboardAsync(string chainKey, string walletIdentifier, CancellationToken cancellationToken = default) =>
        Resolve(chainKey).GetDashboardAsync(chainKey, walletIdentifier, cancellationToken);

    public Task<LiquidTransactionVerificationResult> VerifyAsync(string chainKey, string walletIdentifier, string transactionId, LiquidStakingOperation operation, decimal? expectedAmount, CancellationToken cancellationToken = default) =>
        Resolve(chainKey).VerifyAsync(chainKey, walletIdentifier, transactionId, operation, expectedAmount, cancellationToken);

    private ILiquidStakingGateway Resolve(string chainKey) =>
        registry.TryGet(chainKey, out var chain) && chain.Family == ChainFamily.Solana ? solana : evm;
}
