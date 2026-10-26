using InSalute.Core.Domain;
using InSalute.Core.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InSalute.Tests;

public class UserAdminServiceTests : IAsyncLifetime
{
    private readonly TestEnvironment _env = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _env.DisposeAsync();

    private static CreateUserRequest NewUser(string name, string role) =>
        new(name, name, $"{name}@example.test", role);

    // ---- Elenco e visibilità -------------------------------------------------------------

    [Fact]
    public async Task Operatore_cannot_list_users()
    {
        var operatore = await _env.AddUserAsync("op", Roles.Operatore);

        var result = await _env.AsAsync(operatore, s => s.ListAsync());

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Unauthenticated_caller_is_rejected()
    {
        _env.CurrentUser.UserId = null;
        using var scope = _env.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();

        Assert.False((await service.ListAsync()).Succeeded);
        Assert.False((await service.CreateAsync(NewUser("x", Roles.Operatore))).Succeeded);
    }

    [Fact]
    public async Task Admin_and_Manager_lists_do_not_contain_SuperAdmin()
    {
        var superAdmin = await _env.AddUserAsync("assistenza", Roles.SuperAdmin);
        var admin = await _env.AddUserAsync("admin", Roles.Admin);
        var manager = await _env.AddUserAsync("manager", Roles.Manager);
        await _env.AddUserAsync("op", Roles.Operatore);

        var adminList = (await _env.AsAsync(admin, s => s.ListAsync())).Value!;
        var managerList = (await _env.AsAsync(manager, s => s.ListAsync())).Value!;

        Assert.DoesNotContain(adminList, u => u.Id == superAdmin.Id || u.Role == Roles.SuperAdmin);
        Assert.Equal(3, adminList.Count);
        Assert.DoesNotContain(managerList, u => u.Role != Roles.Operatore);
        Assert.Single(managerList);
    }

    [Fact]
    public async Task SuperAdmin_sees_everyone_including_self()
    {
        var superAdmin = await _env.AddUserAsync("assistenza", Roles.SuperAdmin);
        await _env.AddUserAsync("admin", Roles.Admin);

        var list = (await _env.AsAsync(superAdmin, s => s.ListAsync())).Value!;

        Assert.Contains(list, u => u.Id == superAdmin.Id && u.IsCurrentUser);
        Assert.Equal(2, list.Count);
    }

    // ---- Creazione -----------------------------------------------------------------------

    [Fact]
    public async Task SuperAdmin_creates_Admin_who_creates_Manager_who_creates_Operatore_with_temporary_password()
    {
        var superAdmin = await _env.AddUserAsync("assistenza", Roles.SuperAdmin);

        var admin = await _env.AsAsync(superAdmin, s => s.CreateAsync(NewUser("admin", Roles.Admin)));
        Assert.True(admin.Succeeded, admin.Error);
        var adminUser = await _env.ReloadAsync(admin.Value!.Id);

        // L'Admin appena creato deve prima cambiare la password temporanea.
        Assert.False((await _env.AsAsync(adminUser, s => s.CreateAsync(NewUser("manager", Roles.Manager)))).Succeeded);

        adminUser = await ClearMustChangeAsync(adminUser.Id);
        var manager = await _env.AsAsync(adminUser, s => s.CreateAsync(NewUser("manager", Roles.Manager)));
        Assert.True(manager.Succeeded, manager.Error);

        var managerUser = await ClearMustChangeAsync(manager.Value!.Id);
        var operatore = await _env.AsAsync(managerUser, s => s.CreateAsync(NewUser("op", Roles.Operatore)));
        Assert.True(operatore.Succeeded, operatore.Error);

        var created = await _env.ReloadAsync(operatore.Value!.Id);
        Assert.True(created.MustChangePassword);
        Assert.True(created.IsActive);
        Assert.Equal(12, operatore.Value.TemporaryPassword.Length);
        Assert.Equal([Roles.Operatore], await _env.RolesOfAsync(created));

        using var scope = _env.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.CheckPasswordAsync(created, operatore.Value.TemporaryPassword));
    }

    [Fact]
    public async Task Manager_cannot_create_Admin_or_Manager()
    {
        var manager = await _env.AddUserAsync("manager", Roles.Manager);

        Assert.False((await _env.AsAsync(manager, s => s.CreateAsync(NewUser("a", Roles.Admin)))).Succeeded);
        Assert.False((await _env.AsAsync(manager, s => s.CreateAsync(NewUser("m", Roles.Manager)))).Succeeded);
        Assert.False((await _env.AsAsync(manager, s => s.CreateAsync(NewUser("s", Roles.SuperAdmin)))).Succeeded);
        Assert.True((await _env.AsAsync(manager, s => s.CreateAsync(NewUser("o", Roles.Operatore)))).Succeeded);
    }

    [Fact]
    public async Task Admin_cannot_create_SuperAdmin_but_SuperAdmin_can()
    {
        var admin = await _env.AddUserAsync("admin", Roles.Admin);
        var superAdmin = await _env.AddUserAsync("assistenza", Roles.SuperAdmin);

        Assert.False((await _env.AsAsync(admin, s => s.CreateAsync(NewUser("s1", Roles.SuperAdmin)))).Succeeded);
        Assert.True((await _env.AsAsync(superAdmin, s => s.CreateAsync(NewUser("s2", Roles.SuperAdmin)))).Succeeded);
    }

    [Fact]
    public async Task Operatore_cannot_create_users()
    {
        var operatore = await _env.AddUserAsync("op", Roles.Operatore);

        Assert.False((await _env.AsAsync(operatore, s => s.CreateAsync(NewUser("x", Roles.Operatore)))).Succeeded);
    }

    [Fact]
    public async Task Creating_a_user_with_unknown_role_or_duplicate_username_is_rejected()
    {
        var admin = await _env.AddUserAsync("admin", Roles.Admin);

        Assert.False((await _env.AsAsync(admin, s => s.CreateAsync(NewUser("x", "Boss")))).Succeeded);
        var duplicate = await _env.AsAsync(admin, s => s.CreateAsync(
            new CreateUserRequest("Altro", "admin", "altro@example.test", Roles.Operatore)));
        Assert.False(duplicate.Succeeded);
        Assert.Equal("Questo nome utente è già in uso.", duplicate.Error);
    }

    // ---- Modifica, ruoli ---------------------------------------------------------------------

    [Fact]
    public async Task Admin_cannot_edit_deactivate_or_reset_a_SuperAdmin_even_knowing_the_id()
    {
        var admin = await _env.AddUserAsync("admin", Roles.Admin);
        var superAdmin = await _env.AddUserAsync("assistenza", Roles.SuperAdmin);

        var edit = await _env.AsAsync(admin, s => s.UpdateAsync(
            new UpdateUserRequest(superAdmin.Id, "Hack", "assistenza", "hack@example.test", Roles.SuperAdmin)));
        var deactivate = await _env.AsAsync(admin, s => s.SetActiveAsync(superAdmin.Id, false));
        var reset = await _env.AsAsync(admin, s => s.ResetPasswordAsync(superAdmin.Id));

        Assert.False(edit.Succeeded);
        Assert.False(deactivate.Succeeded);
        Assert.False(reset.Succeeded);

        // Stessa risposta di un utente inesistente: l'esistenza del SuperAdmin non si deduce.
        var missing = await _env.AsAsync(admin, s => s.SetActiveAsync("id-inesistente", false));
        Assert.Equal(missing.Error, deactivate.Error);

        var after = await _env.ReloadAsync(superAdmin.Id);
        Assert.True(after.IsActive);
        Assert.Equal("assistenza", after.DisplayName);
    }

    [Fact]
    public async Task Admin_cannot_assign_SuperAdmin_role()
    {
        var admin = await _env.AddUserAsync("admin", Roles.Admin);
        var operatore = await _env.AddUserAsync("op", Roles.Operatore);

        var result = await _env.AsAsync(admin, s => s.UpdateAsync(
            new UpdateUserRequest(operatore.Id, "op", "op", "op@example.test", Roles.SuperAdmin)));

        Assert.False(result.Succeeded);
        Assert.Equal([Roles.Operatore], await _env.RolesOfAsync(operatore));
    }

    [Fact]
    public async Task Admin_can_change_role_between_Operatore_Manager_and_Admin()
    {
        var admin = await _env.AddUserAsync("admin", Roles.Admin);
        var operatore = await _env.AddUserAsync("op", Roles.Operatore);

        var result = await _env.AsAsync(admin, s => s.UpdateAsync(
            new UpdateUserRequest(operatore.Id, "Op Nuovo", "op", "op@example.test", Roles.Manager)));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal([Roles.Manager], await _env.RolesOfAsync(operatore));
        Assert.Equal("Op Nuovo", (await _env.ReloadAsync(operatore.Id)).DisplayName);
    }

    [Fact]
    public async Task Manager_cannot_change_roles_nor_touch_Manager_or_Admin()
    {
        var manager = await _env.AddUserAsync("manager", Roles.Manager);
        var otherManager = await _env.AddUserAsync("manager2", Roles.Manager);
        var admin = await _env.AddUserAsync("admin", Roles.Admin);
        var operatore = await _env.AddUserAsync("op", Roles.Operatore);

        var promote = await _env.AsAsync(manager, s => s.UpdateAsync(
            new UpdateUserRequest(operatore.Id, "op", "op", "op@example.test", Roles.Admin)));
        var disableManager = await _env.AsAsync(manager, s => s.SetActiveAsync(otherManager.Id, false));
        var disableAdmin = await _env.AsAsync(manager, s => s.SetActiveAsync(admin.Id, false));
        var resetAdmin = await _env.AsAsync(manager, s => s.ResetPasswordAsync(admin.Id));

        Assert.False(promote.Succeeded);
        Assert.False(disableManager.Succeeded);
        Assert.False(disableAdmin.Succeeded);
        Assert.False(resetAdmin.Succeeded);
        Assert.True((await _env.ReloadAsync(otherManager.Id)).IsActive);
        Assert.True((await _env.ReloadAsync(admin.Id)).IsActive);
        Assert.Equal([Roles.Operatore], await _env.RolesOfAsync(operatore));
    }

    [Fact]
    public async Task User_cannot_change_own_role_or_username()
    {
        var admin = await _env.AddUserAsync("admin", Roles.Admin);
        await _env.AddUserAsync("admin2", Roles.Admin);

        var role = await _env.AsAsync(admin, s => s.UpdateAsync(
            new UpdateUserRequest(admin.Id, "admin", "admin", "admin@example.test", Roles.Manager)));
        var name = await _env.AsAsync(admin, s => s.UpdateAsync(
            new UpdateUserRequest(admin.Id, "admin", "nuovo", "admin@example.test", Roles.Admin)));
        var displayName = await _env.AsAsync(admin, s => s.UpdateAsync(
            new UpdateUserRequest(admin.Id, "Nuovo Nome", "admin", "admin@example.test", Roles.Admin)));

        Assert.False(role.Succeeded);
        Assert.False(name.Succeeded);
        Assert.True(displayName.Succeeded, displayName.Error);
    }

    // ---- Disattivazione e ultimo Admin ---------------------------------------------------------

    [Fact]
    public async Task User_cannot_deactivate_self()
    {
        var admin = await _env.AddUserAsync("admin", Roles.Admin);
        await _env.AddUserAsync("admin2", Roles.Admin);

        var result = await _env.AsAsync(admin, s => s.SetActiveAsync(admin.Id, false));

        Assert.False(result.Succeeded);
        Assert.True((await _env.ReloadAsync(admin.Id)).IsActive);
    }

    [Fact]
    public async Task Last_active_Admin_cannot_be_deactivated_even_if_a_SuperAdmin_is_active()
    {
        var superAdmin = await _env.AddUserAsync("assistenza", Roles.SuperAdmin);
        var admin = await _env.AddUserAsync("admin", Roles.Admin);

        var result = await _env.AsAsync(superAdmin, s => s.SetActiveAsync(admin.Id, false));

        Assert.False(result.Succeeded);
        Assert.Equal("Deve esistere almeno un amministratore attivo.", result.Error);
        Assert.True((await _env.ReloadAsync(admin.Id)).IsActive);
    }

    [Fact]
    public async Task Last_active_Admin_cannot_be_demoted()
    {
        var superAdmin = await _env.AddUserAsync("assistenza", Roles.SuperAdmin);
        var admin = await _env.AddUserAsync("admin", Roles.Admin);

        var result = await _env.AsAsync(superAdmin, s => s.UpdateAsync(
            new UpdateUserRequest(admin.Id, "admin", "admin", "admin@example.test", Roles.Manager)));

        Assert.False(result.Succeeded);
        Assert.Equal([Roles.Admin], await _env.RolesOfAsync(admin));
    }

    [Fact]
    public async Task Admin_can_be_deactivated_when_another_active_Admin_exists_and_reactivated()
    {
        var admin1 = await _env.AddUserAsync("admin1", Roles.Admin);
        var admin2 = await _env.AddUserAsync("admin2", Roles.Admin);

        Assert.True((await _env.AsAsync(admin1, s => s.SetActiveAsync(admin2.Id, false))).Succeeded);
        Assert.False((await _env.ReloadAsync(admin2.Id)).IsActive);
        Assert.True((await _env.AsAsync(admin1, s => s.SetActiveAsync(admin2.Id, true))).Succeeded);
        Assert.True((await _env.ReloadAsync(admin2.Id)).IsActive);
    }

    [Fact]
    public async Task Deactivated_actor_cannot_use_the_service()
    {
        var admin1 = await _env.AddUserAsync("admin1", Roles.Admin);
        var admin2 = await _env.AddUserAsync("admin2", Roles.Admin, isActive: false);

        Assert.False((await _env.AsAsync(admin2, s => s.ListAsync())).Succeeded);
        Assert.False((await _env.AsAsync(admin2, s => s.SetActiveAsync(admin1.Id, false))).Succeeded);
    }

    [Fact]
    public async Task Actor_who_must_change_password_cannot_use_the_service()
    {
        var admin = await _env.AddUserAsync("admin", Roles.Admin, mustChangePassword: true);

        Assert.False((await _env.AsAsync(admin, s => s.ListAsync())).Succeeded);
    }

    [Fact]
    public async Task Deactivation_invalidates_the_security_stamp()
    {
        var admin1 = await _env.AddUserAsync("admin1", Roles.Admin);
        var admin2 = await _env.AddUserAsync("admin2", Roles.Admin);
        var before = (await _env.ReloadAsync(admin2.Id)).SecurityStamp;

        await _env.AsAsync(admin1, s => s.SetActiveAsync(admin2.Id, false));

        Assert.NotEqual(before, (await _env.ReloadAsync(admin2.Id)).SecurityStamp);
    }

    // ---- Reimpostazione password ---------------------------------------------------------------

    [Fact]
    public async Task Reset_password_sets_new_temporary_password_and_forces_change()
    {
        var manager = await _env.AddUserAsync("manager", Roles.Manager);
        var operatore = await _env.AddUserAsync("op", Roles.Operatore);

        var result = await _env.AsAsync(manager, s => s.ResetPasswordAsync(operatore.Id));

        Assert.True(result.Succeeded, result.Error);
        var after = await _env.ReloadAsync(operatore.Id);
        Assert.True(after.MustChangePassword);
        using var scope = _env.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.CheckPasswordAsync(after, result.Value!));
        Assert.False(await users.CheckPasswordAsync(after, TestEnvironment.DefaultPassword));
    }

    // ---- Profilo ---------------------------------------------------------------------------

    [Fact]
    public async Task Profile_returns_the_current_user_data()
    {
        var operatore = await _env.AddUserAsync("op", Roles.Operatore);

        var profile = (await _env.AsAsync(operatore, s => s.GetProfileAsync())).Value!;

        Assert.Equal("op", profile.UserName);
        Assert.Equal(Roles.Operatore, profile.Role);
    }

    private async Task<ApplicationUser> ClearMustChangeAsync(string userId)
    {
        using var scope = _env.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(userId))!;
        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        return user;
    }
}
