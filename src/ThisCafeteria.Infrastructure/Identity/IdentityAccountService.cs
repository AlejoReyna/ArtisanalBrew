using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Infrastructure.Identity;

/// <summary>ASP.NET Identity adapter for the Application account boundary.</summary>
public sealed class IdentityAccountService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IWalletIdentityRepository walletIdentities) : IIdentityAccountService
{
    public async Task<IdentityAccount?> FindByIdAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(accountId);
        return user is null ? null : ToAccount(user);
    }

    public async Task<WalletAccountResult> FindOrCreateAndLinkWalletAsync(
        string walletAddress,
        int? walletChainId,
        string? walletProvider,
        string walletFamily,
        CancellationToken cancellationToken = default)
    {
        var isEvm = string.Equals(walletFamily, "Evm", StringComparison.OrdinalIgnoreCase);
        var normalizedAddress = isEvm ? walletAddress.ToLowerInvariant() : walletAddress;
        var existingIdentity = await walletIdentities.FindAsync(walletFamily, normalizedAddress, cancellationToken);
        var user = existingIdentity is null
            ? null
            : await userManager.FindByIdAsync(existingIdentity.UserId)
                ?? throw new InvalidOperationException("The wallet identity points to a missing user.");

        user ??= isEvm
            ? await userManager.Users.SingleOrDefaultAsync(
                candidate => candidate.WalletAddress != null && candidate.WalletAddress.ToLower() == normalizedAddress,
                cancellationToken)
            : await userManager.FindByWalletAddressAsync(walletAddress, cancellationToken);

        user ??= await userManager.FindByNameAsync(CreateUserName(walletAddress, isEvm));
        user ??= await userManager.FindByEmailAsync(CreateSyntheticEmail(walletAddress, isEvm));

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = CreateUserName(walletAddress, isEvm),
                Email = CreateSyntheticEmail(walletAddress, isEvm),
                EmailConfirmed = true
            };

            var create = await userManager.CreateAsync(user);
            if (!create.Succeeded)
            {
                return new WalletAccountResult(false, Error: Describe(create));
            }
        }

        user.WalletAddress = walletAddress;
        user.WalletChainId = walletChainId;
        user.WalletVerifiedAt = DateTimeOffset.UtcNow;

        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            return new WalletAccountResult(false, Error: Describe(update));
        }

        var linked = await walletIdentities.UpsertAsync(
            user.Id,
            walletFamily,
            normalizedAddress,
            walletAddress,
            walletProvider,
            cancellationToken);

        return linked
            ? new WalletAccountResult(true, ToAccount(user))
            : new WalletAccountResult(false, ToAccount(user), WalletAlreadyLinkedToAnotherAccount: true);
    }

    public async Task<IdentityOperationResult> DeleteAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(accountId);
        if (user is null)
        {
            return new IdentityOperationResult(false, "Your session has expired. Please log in again.");
        }

        var result = await userManager.DeleteAsync(user);
        return result.Succeeded
            ? new IdentityOperationResult(true)
            : new IdentityOperationResult(false, Describe(result));
    }

    public async Task<bool> PasswordSignInAsync(string email, string password, bool isPersistent) =>
        (await signInManager.PasswordSignInAsync(email, password, isPersistent, lockoutOnFailure: true)).Succeeded;

    public async Task SignInAsync(string accountId, bool isPersistent)
    {
        var user = await userManager.FindByIdAsync(accountId)
            ?? throw new InvalidOperationException("The wallet identity points to a missing user.");
        await signInManager.SignInAsync(user, isPersistent);
    }

    public Task SignOutAsync() => signInManager.SignOutAsync();

    private static IdentityAccount ToAccount(ApplicationUser user) =>
        new(user.Id, user.UserProfileId, user.WalletAddress);

    private static string CreateUserName(string address, bool isEvm) =>
        isEvm ? address : $"solana{address}";

    private static string CreateSyntheticEmail(string address, bool isEvm)
    {
        if (isEvm)
        {
            return $"{address.ToLowerInvariant()}@wallet.thiscafeteria.local";
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address))).ToLowerInvariant();
        return $"solana-{digest}@wallet.invalid";
    }

    private static string Describe(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(error => error.Description));
}
