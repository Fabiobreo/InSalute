using InSalute.Core.Data;
using InSalute.Core.Domain;
using Microsoft.AspNetCore.Identity;

namespace InSalute.Core.Security;

public sealed record Actor(ApplicationUser User, string Role);

/// <summary>
/// Risolve chi sta eseguendo un'operazione: l'Id viene solo da <see cref="ICurrentUser"/> (identità autenticata),
/// ruolo e stato sono riletti dal database. Un utente disattivato o con password temporanea da cambiare non è un attore valido.
/// </summary>
public sealed class ActorProvider(AppDbContext db, UserManager<ApplicationUser> users, ICurrentUser currentUser)
{
    public async Task<Actor?> GetAsync(CancellationToken ct = default)
    {
        var info = await currentUser.GetAsync(ct);
        if (info is null || string.IsNullOrEmpty(info.Id))
        {
            return null;
        }

        // Il DbContext vive quanto il circuito Blazor: ripartire sempre da dati freschi.
        db.ChangeTracker.Clear();

        var user = await users.FindByIdAsync(info.Id);
        if (user is null || !user.IsActive || user.MustChangePassword)
        {
            return null;
        }

        var role = await RoleOfAsync(user);
        return role is null ? null : new Actor(user, role);
    }

    /// <summary>Come <see cref="GetAsync"/>, ma solo se il ruolo è ammesso dalla policy indicata.</summary>
    public async Task<Actor?> GetAllowedAsync(string policy, CancellationToken ct = default)
    {
        var actor = await GetAsync(ct);
        return actor is not null && Policies.RolesByPolicy[policy].Contains(actor.Role) ? actor : null;
    }

    public async Task<string?> RoleOfAsync(ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);
        return roles.Count == 1 ? roles[0] : null;
    }
}
