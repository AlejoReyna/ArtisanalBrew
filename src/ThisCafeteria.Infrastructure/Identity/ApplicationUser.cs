using Microsoft.AspNetCore.Identity;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public Guid? UserProfileId { get; set; }
    public string? WalletAddress { get; set; }
    public int? WalletChainId { get; set; }
    public DateTimeOffset? WalletVerifiedAt { get; set; }
    public ICollection<WalletIdentity> WalletIdentities { get; set; } = new List<WalletIdentity>();
}
