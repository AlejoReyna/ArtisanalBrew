using Microsoft.AspNetCore.Identity;

namespace ThisCafeteria.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public Guid? UserProfileId { get; set; }
}
