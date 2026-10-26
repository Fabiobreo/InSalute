using InSalute.Core.Data;
using InSalute.Core.Domain;
using InSalute.Core.Security;
using InSalute.Core.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Microsoft.Extensions.Logging;

namespace InSalute.Tests;

public sealed class FakeCurrentUser : ICurrentUser
{
    public string? UserId { get; set; }

    public Task<CurrentUserInfo?> GetAsync(CancellationToken ct = default) =>
        Task.FromResult(UserId is null ? null : new CurrentUserInfo(UserId, null, null));
}

/// <summary>Database SQLite in-memory con Identity configurato come in produzione.</summary>
public sealed class TestEnvironment : IAsyncDisposable
{
    public const string DefaultPassword = "Password12345";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _root;

    public FakeCurrentUser CurrentUser { get; } = new();

    public TestEnvironment()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddInSaluteIdentity();
        services.AddSingleton<ICurrentUser>(CurrentUser);
        _root = services.BuildServiceProvider();

        using var scope = _root.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            roles.CreateAsync(new IdentityRole(role)).GetAwaiter().GetResult();
        }
    }

    /// <summary>Ogni chiamata restituisce un nuovo scope: contesto e servizi non condividono lo stato tra un'operazione e l'altra.</summary>
    public IServiceScope CreateScope() => _root.CreateScope();

    public async Task<ApplicationUser> AddUserAsync(string userName, string role, bool isActive = true, bool mustChangePassword = false)
    {
        using var scope = CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = $"{userName}@example.test",
            DisplayName = userName,
            IsActive = isActive,
            MustChangePassword = mustChangePassword,
            LockoutEnabled = true,
        };
        Assert.True((await users.CreateAsync(user, DefaultPassword)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    /// <summary>Esegue un'operazione come l'utente indicato, con un servizio nuovo.</summary>
    public async Task<T> AsAsync<T>(ApplicationUser actor, Func<UserAdminService, Task<T>> action)
    {
        CurrentUser.UserId = actor.Id;
        using var scope = CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<UserAdminService>());
    }

    public async Task<ApplicationUser> ReloadAsync(string userId)
    {
        using var scope = CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.FindByIdAsync(userId))!;
    }

    public async Task<IList<string>> RolesOfAsync(ApplicationUser user)
    {
        using var scope = CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetRolesAsync(user);
    }

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
