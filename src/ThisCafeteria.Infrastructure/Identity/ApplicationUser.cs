using Microsoft.AspNetCore.Identity;

namespace ThisCafeteria.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public Guid? UserProfileId { get; set; }
    public string? WalletAddress { get; set; }
    public int? WalletChainId { get; set; }
    public DateTimeOffset? WalletVerifiedAt { get; set; }
}
