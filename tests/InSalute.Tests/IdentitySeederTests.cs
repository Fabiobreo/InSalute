using InSalute.Core.Data;
using InSalute.Core.Domain;
using InSalute.Core.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace InSalute.Tests;

public class IdentitySeederTests
{
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task First_start_creates_SuperAdmin_with_generated_password_logged_once_and_forced_change()
    {
        await using var env = new TestEnvironment();
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(logs));
        var options = new BootstrapOptions { SuperAdminUserName = "assistenza" };

        // Due avvii consecutivi: il secondo non deve creare altro né riscrivere la password.
        for (var i = 0; i < 2; i++)
        {
            using var scope = env.CreateScope();
            var sp = scope.ServiceProvider;
            var seeder = new IdentitySeeder(
                sp.GetRequiredService<AppDbContext>(),
                sp.GetRequiredService<UserManager<ApplicationUser>>(),
                sp.GetRequiredService<RoleManager<IdentityRole>>(),
                loggerFactory.CreateLogger<IdentitySeeder>());
            await seeder.SeedAsync(options);
        }

        using var check = env.CreateScope();
        var users = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = Assert.Single(await check.ServiceProvider.GetRequiredService<AppDbContext>().Users.ToListAsync());
        Assert.Equal("assistenza", user.UserName);
        Assert.True(user.MustChangePassword);
        Assert.True(user.IsActive);
        Assert.Equal([Roles.SuperAdmin], await users.GetRolesAsync(user));
        Assert.False(await users.CheckPasswordAsync(user, "Password"));

        var message = Assert.Single(logs.Messages);
        Assert.Contains("assistenza", message);
        var password = System.Text.RegularExpressions.Regex.Match(message, "temporanea '([^']+)'").Groups[1].Value;
        Assert.Equal(12, password.Length);
        Assert.True(await users.CheckPasswordAsync(user, password));
    }
}
