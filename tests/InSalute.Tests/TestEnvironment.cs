using InSalute.Core;
using InSalute.Core.Dispatching;
using InSalute.Core.Data;
using InSalute.Core.Email;
using InSalute.Core.Domain;
using InSalute.Core.Security;
using InSalute.Core.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InSalute.Tests;

/// <summary>Registra i messaggi e può essere configurato per fallire.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    /// <summary>Copia del contenuto degli allegati di ogni messaggio inviato, fatta al momento dell'invio (i file temporanei poi spariscono).</summary>
    public List<IReadOnlyList<(string FileName, byte[] Content)>> SentAttachments { get; } = [];

    public SendResult? FailWith { get; set; }

    /// <summary>Se impostato, dopo questo numero di messaggi inviati con successo i successivi falliscono (connessione caduta).</summary>
    public int? FailAfter { get; set; }

    public Task<SendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (FailWith is not null)
        {
            return Task.FromResult(FailWith);
        }
        if (FailAfter is { } limit && Sent.Count >= limit)
        {
            return Task.FromResult(SendResult.Fail(SendErrorCode.ConnectionFailed, "simulato"));
        }
        Sent.Add(message);
        SentAttachments.Add((message.Attachments ?? [])
            .Select(a => (a.FileName, File.ReadAllBytes(a.FilePath)))
            .ToList());
        return Task.FromResult(SendResult.Ok());
    }
}

public sealed class FakeCurrentUser : ICurrentUser
{
    public string? UserId { get; set; }

    public Task<CurrentUserInfo?> GetAsync(CancellationToken ct = default) =>
        Task.FromResult(UserId is null ? null : new CurrentUserInfo(UserId, null, null));
}

/// <summary>Orologio controllabile dai test.</summary>
public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Rallenta le letture del database: con SQLite in memoria le query finiscono quasi subito e due operazioni
/// sovrapposte sullo stesso DbContext (errore che su SQL Server capita davvero) non si vedrebbero.
/// </summary>
public sealed class QueryDelay : DbCommandInterceptor
{
    public TimeSpan Delay { get; set; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }
        return result;
    }
}

/// <summary>Database SQLite in-memory con Identity configurato come in produzione.</summary>
public sealed class TestEnvironment : IAsyncDisposable
{
    public const string DefaultPassword = "Password12345";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _root;

    public FakeCurrentUser CurrentUser { get; } = new();

    public FakeEmailSender EmailSender { get; } = new();

    public TestClock Clock { get; } = new();

    public QueryDelay QueryDelay { get; } = new();

    /// <summary>Cartella temporanea dei file in lavorazione, solo per questo ambiente.</summary>
    public string TempPath { get; } = Path.Combine(Path.GetTempPath(), "insalute-tests", Guid.NewGuid().ToString("N"));

    public RetentionOptions Retention { get; } = new();

    /// <summary>Si può cambiare prima di usare i servizi (ad esempio il tempo massimo di attesa dei caricamenti).</summary>
    public StorageOptions Storage { get; }

    public TestEnvironment()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection).AddInterceptors(QueryDelay));
        services.AddInSaluteIdentity();
        services.AddSingleton<ICurrentUser>(CurrentUser);
        services.AddInSaluteSettings();
        services.AddSingleton<IEmailSender>(EmailSender);
        services.AddSingleton<TimeProvider>(Clock);
        Storage = new StorageOptions { TempPath = TempPath };
        services.AddSingleton<IOptions<StorageOptions>>(Options.Create(Storage));
        services.AddSingleton<IOptions<RetentionOptions>>(Options.Create(Retention));
        services.AddInSaluteDispatching();
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
        if (Directory.Exists(TempPath))
        {
            Directory.Delete(TempPath, recursive: true);
        }
    }
}
