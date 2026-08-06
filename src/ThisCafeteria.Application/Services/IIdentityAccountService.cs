namespace ThisCafeteria.Application.Services;

/// <summary>
/// Boundary for account-store operations needed by delivery mechanisms. ASP.NET Identity and its
/// result types stay behind this contract in Infrastructure.
/// </summary>
public interface IIdentityAccountService
{
    Task<IdentityAccount?> FindByIdAsync(string accountId, CancellationToken cancellationToken = default);
    Task<WalletAccountResult> FindOrCreateAndLinkWalletAsync(
        string walletAddress,
        int? walletChainId,
        string? walletProvider,
        string walletFamily,
        CancellationToken cancellationToken = default);
    Task<IdentityOperationResult> DeleteAsync(string accountId, CancellationToken cancellationToken = default);
    Task<bool> PasswordSignInAsync(string email, string password, bool isPersistent);
    Task SignInAsync(string accountId, bool isPersistent);
    Task SignOutAsync();
}

public sealed record IdentityAccount(string Id, Guid? UserProfileId, string? WalletAddress);

public sealed record IdentityOperationResult(bool Succeeded, string? Error = null);

public sealed record WalletAccountResult(
    bool Succeeded,
    IdentityAccount? Account = null,
    bool WalletAlreadyLinkedToAnotherAccount = false,
    string? Error = null);
