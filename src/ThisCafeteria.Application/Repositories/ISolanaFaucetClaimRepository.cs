using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Repositories;

/// <summary>
/// Records devnet CAFE faucet mints, which is how the faucet's cooldown is enforced - a bare
/// Token-2022 mint has no rate limit of its own.
/// </summary>
public interface ISolanaFaucetClaimRepository
{
    /// <summary>The wallet's most recent claim on a chain, or null if it has never claimed.</summary>
    Task<SolanaFaucetClaim?> FindLatestAsync(
        string chainKey,
        string walletAddress,
        CancellationToken cancellationToken = default);

    Task AddAsync(SolanaFaucetClaim claim, CancellationToken cancellationToken = default);
}
