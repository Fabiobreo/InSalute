using InSalute.Core.Data;
using InSalute.Core.Domain;
using InSalute.Core.Email;
using InSalute.Core.Settings;
using InSalute.Core.Texts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InSalute.Tests;

public class SmtpSettingsServiceTests : IAsyncLifetime
{
    private const string SecretPassword = "Sup3rSegreta-Pwd!";

    private readonly TestEnvironment _env = new();

    public async Task InitializeAsync()
    {
        var admin = await _env.AddUserAsync("admin", Roles.Admin);
        _env.CurrentUser.UserId = admin.Id;
    }

    public async Task DisposeAsync() => await _env.DisposeAsync();

    private static SmtpSettingsInput Input(string? password = SecretPassword, string userName = "info@example.test") =>
        new("smtp.example.test", 465, SmtpSecurity.SslOnConnect, userName, password, "info@example.test", "Poliambulatorio");

    private async Task<T> RunAsync<T>(Func<SmtpSettingsService, Task<T>> action)
    {
        using var scope = _env.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<SmtpSettingsService>());
    }

    private async Task<SmtpSettings> StoredAsync()
    {
        using var scope = _env.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().SmtpSettings.AsNoTracking().SingleAsync();
    }

    [Fact]
    public async Task Not_configured_until_saved()
    {
        Assert.False(await RunAsync(s => s.IsConfiguredAsync()));
        Assert.Null((await RunAsync(s => s.GetAsync())).Value);

        Assert.True((await RunAsync(s => s.SaveAsync(Input()))).Succeeded);

        Assert.True(await RunAsync(s => s.IsConfiguredAsync()));
    }

    [Fact]
    public async Task Password_is_stored_encrypted_and_decrypts_with_the_purpose()
    {
        await RunAsync(s => s.SaveAsync(Input()));

        var stored = await StoredAsync();
        Assert.NotEmpty(stored.ProtectedPassword);
        Assert.DoesNotContain(SecretPassword, stored.ProtectedPassword);
        Assert.NotEqual(SecretPassword, stored.ProtectedPassword);

        using var scope = _env.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        Assert.Equal(SecretPassword, provider.CreateProtector(SmtpSettingsService.PasswordPurpose).Unprotect(stored.ProtectedPassword));
        Assert.ThrowsAny<Exception>(() => provider.CreateProtector("altro.scopo").Unprotect(stored.ProtectedPassword));
    }

    [Fact]
    public async Task Password_is_never_returned_to_the_UI()
    {
        await RunAsync(s => s.SaveAsync(Input()));

        var view = (await RunAsync(s => s.GetAsync())).Value!;

        Assert.True(view.HasPassword);
        Assert.DoesNotContain(typeof(SmtpSettingsView).GetProperties(), p => p.Name.Contains("Password") && p.PropertyType == typeof(string));
        Assert.DoesNotContain(SecretPassword, System.Text.Json.JsonSerializer.Serialize(view));
    }

    [Fact]
    public async Task Empty_new_password_keeps_the_current_one_and_a_new_one_replaces_it()
    {
        await RunAsync(s => s.SaveAsync(Input()));
        var first = (await StoredAsync()).ProtectedPassword;

        Assert.True((await RunAsync(s => s.SaveAsync(Input(password: null) with { Port = 587, Security = SmtpSecurity.StartTls, FromName = "Altro" }))).Succeeded);
        var kept = await StoredAsync();
        Assert.Equal(first, kept.ProtectedPassword);
        Assert.Equal(587, kept.Port);

        Assert.True((await RunAsync(s => s.SaveAsync(Input(password: "NuovaPassword99")))).Succeeded);
        Assert.NotEqual(first, (await StoredAsync()).ProtectedPassword);
    }

    [Theory]
    [InlineData("server-attaccante.example.test", "info@example.test")]
    [InlineData("smtp.example.test", "altro@example.test")]
    public async Task Changing_host_or_user_name_requires_the_password_again(string host, string userName)
    {
        await RunAsync(s => s.SaveAsync(Input()));
        var before = await StoredAsync();

        var withoutPassword = await RunAsync(s => s.SaveAsync(Input(password: null, userName: userName) with { Host = host }));

        Assert.False(withoutPassword.Succeeded);
        Assert.Equal(Messaggi.PasswordSmtpDaReinserire, withoutPassword.Error);
        var unchanged = await StoredAsync();
        Assert.Equal(before.Host, unchanged.Host);
        Assert.Equal(before.UserName, unchanged.UserName);

        var withPassword = await RunAsync(s => s.SaveAsync(Input(password: "NuovaPassword99", userName: userName) with { Host = host }));
        Assert.True(withPassword.Succeeded, withPassword.Error);
        Assert.Equal(host, (await StoredAsync()).Host);
    }

    [Fact]
    public void Host_and_user_name_are_compared_ignoring_case_and_spaces()
    {
        Assert.False(SmtpSettingsService.RequiresNewPassword("smtp.example.test", "Info@Example.test", " SMTP.example.test ", "info@example.test"));
        Assert.True(SmtpSettingsService.RequiresNewPassword("smtp.example.test", "info@example.test", "smtp.example.test.evil", "info@example.test"));
    }

    [Fact]
    public async Task A_user_name_without_any_password_is_rejected_but_a_server_without_authentication_is_allowed()
    {
        Assert.False((await RunAsync(s => s.SaveAsync(Input(password: null)))).Succeeded);

        var anonymous = await RunAsync(s => s.SaveAsync(Input(password: null, userName: "")));
        Assert.True(anonymous.Succeeded, anonymous.Error);
        Assert.Equal("", (await StoredAsync()).ProtectedPassword);
    }

    [Theory]
    [InlineData("", 465, "info@example.test")]
    [InlineData("smtp.example.test", 0, "info@example.test")]
    [InlineData("smtp.example.test", 65536, "info@example.test")]
    [InlineData("smtp.example.test", 465, "non-una-email")]
    [InlineData("smtp.example.test", 465, "")]
    public async Task Invalid_settings_are_rejected_and_not_saved(string host, int port, string from)
    {
        var result = await RunAsync(s => s.SaveAsync(Input() with { Host = host, Port = port, FromAddress = from }));

        Assert.False(result.Succeeded);
        Assert.False(await RunAsync(s => s.IsConfiguredAsync()));
    }

    [Fact]
    public async Task Undefined_security_mode_is_rejected()
    {
        var result = await RunAsync(s => s.SaveAsync(Input() with { Security = (SmtpSecurity)0 }));

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(Roles.Operatore)]
    [InlineData(Roles.Manager)]
    public async Task Only_Admin_and_SuperAdmin_can_read_save_or_test(string role)
    {
        await RunAsync(s => s.SaveAsync(Input()));
        var user = await _env.AddUserAsync("altro", role);
        _env.CurrentUser.UserId = user.Id;

        Assert.False((await RunAsync(s => s.GetAsync())).Succeeded);
        Assert.False((await RunAsync(s => s.SaveAsync(Input() with { Host = "x.example.test" }))).Succeeded);
        Assert.False((await RunAsync(s => s.SendTestEmailAsync("dest@example.test"))).Succeeded);
        Assert.Empty(_env.EmailSender.Sent);
        Assert.Equal("smtp.example.test", (await StoredAsync()).Host);
    }

    [Fact]
    public async Task Test_email_is_sent_with_the_fixed_subject()
    {
        await RunAsync(s => s.SaveAsync(Input()));

        var result = await RunAsync(s => s.SendTestEmailAsync(" dest@example.test "));

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.Success);
        var message = Assert.Single(_env.EmailSender.Sent);
        Assert.Equal("dest@example.test", message.ToAddress);
        Assert.Equal("Email di prova InSalute", message.Subject);
    }

    [Fact]
    public async Task Test_email_reports_the_sender_failure()
    {
        await RunAsync(s => s.SaveAsync(Input()));
        _env.EmailSender.FailWith = SendResult.Fail(SendErrorCode.AuthenticationFailed, "dettagli");

        var result = await RunAsync(s => s.SendTestEmailAsync("dest@example.test"));

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.Success);
        Assert.Equal(SendErrorCode.AuthenticationFailed, result.Value.ErrorCode);
    }

    [Fact]
    public async Task Test_email_needs_a_valid_address_and_saved_settings()
    {
        Assert.False((await RunAsync(s => s.SendTestEmailAsync("dest@example.test"))).Succeeded);

        await RunAsync(s => s.SaveAsync(Input()));
        Assert.False((await RunAsync(s => s.SendTestEmailAsync("non valido"))).Succeeded);
        Assert.Empty(_env.EmailSender.Sent);
    }
}
