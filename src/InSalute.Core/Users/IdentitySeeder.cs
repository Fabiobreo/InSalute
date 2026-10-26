using InSalute.Core.Data;
using InSalute.Core.Dispatching;
using InSalute.Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InSalute.Core.Users;

public sealed class BootstrapOptions
{
    public string SuperAdminUserName { get; set; } = "assistenza";

    /// <summary>Identity richiede un'email univoca per ogni utente, anche per l'account di assistenza.</summary>
    public string SuperAdminEmail { get; set; } = "assistenza@insalute.invalid";
}

public sealed class IdentitySeeder(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    ILogger<IdentitySeeder> logger)
{
    public async Task SeedAsync(BootstrapOptions options, CancellationToken ct = default)
    {
        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                var result = await roles.CreateAsync(new IdentityRole(role));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"Impossibile creare il ruolo {role}.");
                }
            }
        }

        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        var password = PasswordGenerator.Generate();
        var user = new ApplicationUser
        {
            UserName = options.SuperAdminUserName,
            Email = options.SuperAdminEmail,
            DisplayName = "Assistenza tecnica",
            IsActive = true,
            MustChangePassword = true,
            LockoutEnabled = true,
            CreatedAt = DateTime.UtcNow,
        };

        var created = await users.CreateAsync(user, password);
        if (created.Succeeded)
        {
            created = await users.AddToRoleAsync(user, Roles.SuperAdmin);
        }
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                "Impossibile creare l'account SuperAdmin iniziale: " + string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        // Unico punto in cui una password compare nei log: scritta una sola volta, alla creazione.
        logger.LogWarning(
            "=== PRIMO AVVIO === Creato l'account SuperAdmin '{UserName}' con password temporanea '{Password}'. " +
            "Annotarla ora: non sarà più mostrata. Al primo accesso verrà richiesto di cambiarla.",
            user.UserName, password);
    }
}
