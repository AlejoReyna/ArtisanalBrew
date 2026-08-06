using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ThisCafeteria.Infrastructure.Identity;

/// <summary>
/// Wallet lookups over ASP.NET Identity's user store.
///
/// <see cref="UserManager{TUser}.Users"/> is an <see cref="IQueryable{T}"/>, so querying it
/// asynchronously needs Entity Framework's extension methods. That is a persistence detail, so it
/// lives here rather than in the controllers that need the lookup.
/// </summary>
public static class UserManagerWalletExtensions
{
    public static Task<ApplicationUser?> FindByWalletAddressAsync(
        this UserManager<ApplicationUser> userManager,
        string walletAddress,
        CancellationToken cancellationToken = default) =>
        userManager.Users.SingleOrDefaultAsync(
            user => user.WalletAddress == walletAddress,
            cancellationToken);
}
