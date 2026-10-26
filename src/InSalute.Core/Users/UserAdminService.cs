using InSalute.Core.Data;
using InSalute.Core.Dispatching;
using InSalute.Core.Domain;
using InSalute.Core.Security;
using InSalute.Core.Texts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InSalute.Core.Users;

public sealed record CreateUserRequest(string DisplayName, string UserName, string Email, string Role);

public sealed record UpdateUserRequest(string Id, string DisplayName, string UserName, string Email, string Role);

public sealed record CreatedUser(string Id, string TemporaryPassword);

public sealed record UserListItem(
    string Id,
    string DisplayName,
    string UserName,
    string Email,
    string Role,
    bool IsActive,
    DateTime CreatedAt,
    bool IsCurrentUser,
    bool CanEdit,
    bool CanChangeRole,
    bool CanResetPassword,
    bool CanToggleActive);

public sealed record UserProfile(string DisplayName, string UserName, string Email, string Role);

/// <summary>
/// Gestione utenti. Ogni metodo ricava l'utente corrente solo da <see cref="ICurrentUser"/> (Id dall'identità
/// autenticata), poi ne rilegge ruolo e stato dal database: nessun permesso dipende da parametri della UI.
/// </summary>
public sealed class UserAdminService(AppDbContext db, UserManager<ApplicationUser> users, ICurrentUser currentUser)
{
    private const int MaxDisplayNameLength = 100;

    private sealed record Actor(ApplicationUser User, string Role);

    /// <summary>Ruoli che il ruolo indicato può vedere e gestire (creare, modificare, disattivare, reimpostare la password).</summary>
    public static IReadOnlyList<string> ManageableRoles(string actorRole) => actorRole switch
    {
        Roles.SuperAdmin => Roles.All,
        Roles.Admin => [Roles.Admin, Roles.Manager, Roles.Operatore],
        Roles.Manager => [Roles.Operatore],
        _ => [],
    };

    public async Task<OperationResult<IReadOnlyList<UserListItem>>> ListAsync(CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        if (actor is null)
        {
            return OperationResult<IReadOnlyList<UserListItem>>.Fail(Messaggi.NonAutorizzato);
        }

        var visibleRoles = ManageableRoles(actor.Role);
        if (visibleRoles.Count == 0)
        {
            return OperationResult<IReadOnlyList<UserListItem>>.Fail(Messaggi.NonAutorizzato);
        }

        // Il filtro sui ruoli visibili è nella query, non in UI.
        var rows = await (
            from u in db.Users.AsNoTracking()
            join ur in db.UserRoles on u.Id equals ur.UserId
            join r in db.Roles on ur.RoleId equals r.Id
            where visibleRoles.Contains(r.Name!)
            orderby u.DisplayName, u.UserName
            select new { u.Id, u.DisplayName, u.UserName, u.Email, Role = r.Name!, u.IsActive, u.CreatedAt })
            .ToListAsync(ct);

        var actorCanChangeRoles = actor.Role is Roles.Admin or Roles.SuperAdmin;
        IReadOnlyList<UserListItem> items = rows.Select(x =>
        {
            var self = x.Id == actor.User.Id;
            return new UserListItem(
                x.Id, x.DisplayName, x.UserName ?? "", x.Email ?? "", x.Role, x.IsActive, x.CreatedAt,
                IsCurrentUser: self,
                CanEdit: true,
                CanChangeRole: actorCanChangeRoles && !self && x.Role != Roles.SuperAdmin,
                CanResetPassword: true,
                CanToggleActive: !self);
        }).ToList();

        return OperationResult<IReadOnlyList<UserListItem>>.Ok(items);
    }

    /// <summary>Ruoli che l'utente corrente può assegnare creando un nuovo utente.</summary>
    public async Task<IReadOnlyList<string>> GetAssignableRolesAsync(CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        return actor is null ? [] : ManageableRoles(actor.Role);
    }

    public async Task<OperationResult<UserProfile>> GetProfileAsync(CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        if (actor is null)
        {
            return OperationResult<UserProfile>.Fail(Messaggi.NonAutorizzato);
        }

        return OperationResult<UserProfile>.Ok(new UserProfile(
            actor.User.DisplayName, actor.User.UserName ?? "", actor.User.Email ?? "", actor.Role));
    }

    public async Task<OperationResult<CreatedUser>> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        if (actor is null)
        {
            return OperationResult<CreatedUser>.Fail(Messaggi.NonAutorizzato);
        }
        if (!Roles.All.Contains(request.Role))
        {
            return OperationResult<CreatedUser>.Fail(Messaggi.RuoloNonValido);
        }
        if (!ManageableRoles(actor.Role).Contains(request.Role))
        {
            return OperationResult<CreatedUser>.Fail(Messaggi.NonAutorizzato);
        }

        var invalid = ValidateFields(request.DisplayName, request.UserName, request.Email);
        if (invalid is not null)
        {
            return OperationResult<CreatedUser>.Fail(invalid);
        }

        var temporaryPassword = PasswordGenerator.Generate();
        var user = new ApplicationUser
        {
            DisplayName = request.DisplayName.Trim(),
            UserName = request.UserName.Trim(),
            Email = request.Email.Trim(),
            IsActive = true,
            MustChangePassword = true,
            LockoutEnabled = true,
            CreatedAt = DateTime.UtcNow,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var created = await users.CreateAsync(user, temporaryPassword);
        if (!created.Succeeded)
        {
            return OperationResult<CreatedUser>.Fail(FirstError(created));
        }

        var roleAdded = await users.AddToRoleAsync(user, request.Role);
        if (!roleAdded.Succeeded)
        {
            return OperationResult<CreatedUser>.Fail(FirstError(roleAdded));
        }

        await tx.CommitAsync(ct);
        return OperationResult<CreatedUser>.Ok(new CreatedUser(user.Id, temporaryPassword));
    }

    public async Task<OperationResult> UpdateAsync(UpdateUserRequest request, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        if (actor is null)
        {
            return OperationResult.Fail(Messaggi.NonAutorizzato);
        }

        var (target, targetRole, error) = await LoadTargetAsync(request.Id, actor);
        if (target is null || targetRole is null)
        {
            return OperationResult.Fail(error!);
        }

        var invalid = ValidateFields(request.DisplayName, request.UserName, request.Email);
        if (invalid is not null)
        {
            return OperationResult.Fail(invalid);
        }

        var isSelf = target.Id == actor.User.Id;
        var newUserName = request.UserName.Trim();
        if (isSelf && !string.Equals(newUserName, target.UserName, StringComparison.Ordinal))
        {
            return OperationResult.Fail(Messaggi.NonPuoiCambiareIlTuoUsername);
        }

        var roleChanges = !string.Equals(request.Role, targetRole, StringComparison.Ordinal);
        if (roleChanges)
        {
            if (isSelf)
            {
                return OperationResult.Fail(Messaggi.NonPuoiCambiareIlTuoRuolo);
            }
            if (actor.Role is not (Roles.Admin or Roles.SuperAdmin))
            {
                return OperationResult.Fail(Messaggi.NonAutorizzato);
            }
            if (!Roles.All.Contains(request.Role))
            {
                return OperationResult.Fail(Messaggi.RuoloNonValido);
            }
            if (targetRole == Roles.SuperAdmin || request.Role == Roles.SuperAdmin)
            {
                return OperationResult.Fail(Messaggi.RuoloSuperAdminNonModificabile);
            }
            if (targetRole == Roles.Admin && target.IsActive && await CountOtherActiveAdminsAsync(target.Id, ct) == 0)
            {
                return OperationResult.Fail(Messaggi.UltimoAdmin);
            }
        }

        target.DisplayName = request.DisplayName.Trim();
        target.UserName = newUserName;
        target.Email = request.Email.Trim();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var updated = await users.UpdateAsync(target);
        if (!updated.Succeeded)
        {
            return OperationResult.Fail(FirstError(updated));
        }

        if (roleChanges)
        {
            var removed = await users.RemoveFromRoleAsync(target, targetRole);
            if (!removed.Succeeded)
            {
                return OperationResult.Fail(FirstError(removed));
            }
            var added = await users.AddToRoleAsync(target, request.Role);
            if (!added.Succeeded)
            {
                return OperationResult.Fail(FirstError(added));
            }
            await users.UpdateSecurityStampAsync(target);
        }

        await tx.CommitAsync(ct);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> SetActiveAsync(string userId, bool isActive, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        if (actor is null)
        {
            return OperationResult.Fail(Messaggi.NonAutorizzato);
        }

        var (target, targetRole, error) = await LoadTargetAsync(userId, actor);
        if (target is null)
        {
            return OperationResult.Fail(error!);
        }

        if (!isActive)
        {
            if (target.Id == actor.User.Id)
            {
                return OperationResult.Fail(Messaggi.NonPuoiDisattivareTeStesso);
            }
            if (targetRole == Roles.Admin && target.IsActive && await CountOtherActiveAdminsAsync(target.Id, ct) == 0)
            {
                return OperationResult.Fail(Messaggi.UltimoAdmin);
            }
        }

        target.IsActive = isActive;
        var updated = await users.UpdateAsync(target);
        if (!updated.Succeeded)
        {
            return OperationResult.Fail(FirstError(updated));
        }

        if (!isActive)
        {
            // Invalida subito cookie e circuiti Blazor già aperti dell'utente disattivato.
            await users.UpdateSecurityStampAsync(target);
        }
        return OperationResult.Ok();
    }

    /// <summary>Genera e imposta una nuova password temporanea; restituita una sola volta.</summary>
    public async Task<OperationResult<string>> ResetPasswordAsync(string userId, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        if (actor is null)
        {
            return OperationResult<string>.Fail(Messaggi.NonAutorizzato);
        }

        var (target, _, error) = await LoadTargetAsync(userId, actor);
        if (target is null)
        {
            return OperationResult<string>.Fail(error!);
        }

        var temporaryPassword = PasswordGenerator.Generate();
        var token = await users.GeneratePasswordResetTokenAsync(target);
        var reset = await users.ResetPasswordAsync(target, token, temporaryPassword);
        if (!reset.Succeeded)
        {
            return OperationResult<string>.Fail(FirstError(reset));
        }

        target.MustChangePassword = true;
        var updated = await users.UpdateAsync(target);
        if (!updated.Succeeded)
        {
            return OperationResult<string>.Fail(FirstError(updated));
        }

        await users.SetLockoutEndDateAsync(target, null);
        await users.ResetAccessFailedCountAsync(target);
        return OperationResult<string>.Ok(temporaryPassword);
    }

    private async Task<Actor?> GetActorAsync(CancellationToken ct)
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

        var role = await GetSingleRoleAsync(user);
        return role is null ? null : new Actor(user, role);
    }

    private async Task<string?> GetSingleRoleAsync(ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);
        return roles.Count == 1 ? roles[0] : null;
    }

    /// <summary>
    /// Carica l'utente su cui agire. Un SuperAdmin è indistinguibile da un utente inesistente
    /// per chi non è SuperAdmin.
    /// </summary>
    private async Task<(ApplicationUser? Target, string? Role, string? Error)> LoadTargetAsync(string? id, Actor actor)
    {
        if (string.IsNullOrEmpty(id))
        {
            return (null, null, Messaggi.UtenteNonTrovato);
        }

        var target = await users.FindByIdAsync(id);
        var role = target is null ? null : await GetSingleRoleAsync(target);
        if (target is null || role is null || (role == Roles.SuperAdmin && actor.Role != Roles.SuperAdmin))
        {
            return (null, null, Messaggi.UtenteNonTrovato);
        }

        if (!ManageableRoles(actor.Role).Contains(role))
        {
            return (null, null, Messaggi.NonAutorizzato);
        }

        return (target, role, null);
    }

    /// <summary>Admin attivi diversi dall'utente indicato (i SuperAdmin non contano).</summary>
    private Task<int> CountOtherActiveAdminsAsync(string excludedUserId, CancellationToken ct) =>
        (from u in db.Users
         join ur in db.UserRoles on u.Id equals ur.UserId
         join r in db.Roles on ur.RoleId equals r.Id
         where r.Name == Roles.Admin && u.IsActive && u.Id != excludedUserId
         select u.Id).CountAsync(ct);

    private static string? ValidateFields(string? displayName, string? userName, string? email)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Messaggi.NomeVisualizzatoObbligatorio;
        }
        if (displayName.Trim().Length > MaxDisplayNameLength)
        {
            return Messaggi.NomeVisualizzatoTroppoLungo;
        }
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Messaggi.UsernameObbligatorio;
        }
        if (string.IsNullOrWhiteSpace(email))
        {
            return Messaggi.EmailObbligatoria;
        }
        return null;
    }

    private static string FirstError(IdentityResult result) =>
        result.Errors.FirstOrDefault()?.Description ?? Messaggi.ErroreGenerico;
}
