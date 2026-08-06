namespace ThisCafeteria.Application.Services.Blockchain;

public sealed record SolanaFaucetClaimResult(
    bool Success,
    string? Signature,
    string? ExplorerUrl,
    decimal Amount,
    string? Error,
    DateTime? NextClaimAtUtc);

/// <summary>
/// Server-side devnet CAFE faucet.
/// </summary>
public interface ISolanaFaucetService
{
    /// <summary>Live claim amount, cooldown, and eligibility for a wallet on the given Solana chain.</summary>
    Task<CafeFaucetStatus> GetStatusAsync(string chainKey, string wallet, CancellationToken cancellationToken = default);

    /// <summary>Mints CAFE to the wallet's associated token account under the administrator authority, enforcing the cooldown.</summary>
    Task<SolanaFaucetClaimResult> ClaimAsync(string chainKey, string wallet, CancellationToken cancellationToken = default);
}
