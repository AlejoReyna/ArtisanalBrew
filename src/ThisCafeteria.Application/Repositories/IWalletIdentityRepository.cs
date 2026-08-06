using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Repositories;

/// <summary>
/// Maps a verified wallet address to the application user that proved ownership of it.
/// </summary>
public interface IWalletIdentityRepository
{
    /// <summary>The identity for an address within a chain family, or null if never verified.</summary>
    Task<WalletIdentity?> FindAsync(
        string family,
        string normalizedAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or refreshes the mapping from an address to a user.
    ///
    /// Returns <c>false</c> when the address is already verified for a <b>different</b> user -
    /// the one case that must not silently succeed, since it would hand one person's wallet
    /// identity to another account.
    /// </summary>
    Task<bool> UpsertAsync(
        string userId,
        string family,
        string normalizedAddress,
        string displayAddress,
        string? walletProvider,
        CancellationToken cancellationToken = default);
}
