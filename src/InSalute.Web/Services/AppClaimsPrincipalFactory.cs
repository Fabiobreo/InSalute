using System.Security.Claims;
using InSalute.Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace InSalute.Web.Services;

internal sealed class AppClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AppClaims.DisplayName, user.DisplayName));
        if (user.MustChangePassword)
        {
            identity.AddClaim(new Claim(AppClaims.MustChangePassword, "true"));
        }
        return identity;
    }
}
