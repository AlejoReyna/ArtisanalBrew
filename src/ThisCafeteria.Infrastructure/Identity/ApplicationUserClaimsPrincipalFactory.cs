using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ThisCafeteria.Infrastructure.Identity;

public sealed class ApplicationUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (!string.IsNullOrWhiteSpace(user.WalletAddress))
        {
            identity.AddClaim(new Claim("wallet_address", user.WalletAddress));

            if (user.WalletChainId is int chainId)
            {
                identity.AddClaim(new Claim(
                    "wallet_chain_id",
                    chainId.ToString(CultureInfo.InvariantCulture)));
            }
        }

        return identity;
    }
}
