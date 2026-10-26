using System.Security.Claims;
using InSalute.Core.Security;
using Microsoft.AspNetCore.Components.Authorization;

namespace InSalute.Web.Services;

internal sealed class CurrentUser(AuthenticationStateProvider authenticationStateProvider) : ICurrentUser
{
    public async Task<CurrentUserInfo?> GetAsync(CancellationToken ct = default)
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        var user = state.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return id is null ? null : new CurrentUserInfo(id, user.Identity.Name, user.FindFirstValue(ClaimTypes.Role));
    }
}
