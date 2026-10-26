using InSalute.Core.Data;
using InSalute.Core.Domain;
using InSalute.Core.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InSalute.Tests;

public class EmailTemplateServiceTests : IAsyncLifetime
{
    private readonly TestEnvironment _env = new();
    private ApplicationUser _admin = default!;

    public async Task InitializeAsync()
    {
        _admin = await _env.AddUserAsync("admin", Roles.Admin);
        _env.CurrentUser.UserId = _admin.Id;
    }

    public async Task DisposeAsync() => await _env.DisposeAsync();

    private async Task<T> RunAsync<T>(Func<EmailTemplateService, Task<T>> action)
    {
        using var scope = _env.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<EmailTemplateService>());
    }

    [Fact]
    public async Task Password_template_without_PASSWORD_is_rejected()
    {
        var result = await RunAsync(s => s.SaveAsync(EmailTemplateKind.Password, "Credenziali", "Buongiorno {NOME}"));

        Assert.False(result.Succeeded);
        Assert.Equal("Il modello deve contenere {PASSWORD}.", result.Error);
    }

    [Theory]
    [InlineData("Referto", "La password è {PASSWORD}")]
    [InlineData("Referto {PASSWORD}", "Buongiorno {NOME}")]
    public async Task Report_template_with_PASSWORD_is_rejected(string subject, string body)
    {
        var result = await RunAsync(s => s.SaveAsync(EmailTemplateKind.Report, subject, body));

        Assert.False(result.Succeeded);
        Assert.Equal("Il modello del referto non può contenere la password.", result.Error);
    }

    [Theory]
    [InlineData(EmailTemplateKind.Report, "Referto", "Ciao {NOME} {XYZ}")]
    [InlineData(EmailTemplateKind.Report, "Referto {DATA}", "Ciao")]
    [InlineData(EmailTemplateKind.Password, "Credenziali", "Ciao {nome}: {PASSWORD}")]
    public async Task Unknown_placeholder_is_rejected(EmailTemplateKind kind, string subject, string body)
    {
        var result = await RunAsync(s => s.SaveAsync(kind, subject, body));

        Assert.False(result.Succeeded);
        Assert.StartsWith("Segnaposto non riconosciuto: {", result.Error);
    }

    [Theory]
    [InlineData("", "corpo")]
    [InlineData("oggetto", "   ")]
    public async Task Empty_subject_or_body_is_rejected(string subject, string body)
    {
        var result = await RunAsync(s => s.SaveAsync(EmailTemplateKind.Report, subject, body));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Rejected_templates_do_not_change_the_stored_ones()
    {
        await RunAsync(s => s.SaveAsync(EmailTemplateKind.Password, "X", "senza segnaposto"));

        using var scope = _env.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().EmailTemplates
            .SingleAsync(t => t.Kind == EmailTemplateKind.Password);
        Assert.Equal("Credenziali", stored.Subject);
        Assert.Contains("{PASSWORD}", stored.Body);
    }

    [Fact]
    public async Task Valid_templates_are_saved_with_author_and_date()
    {
        var password = await RunAsync(s => s.SaveAsync(
            EmailTemplateKind.Password, "Password per {COGNOME}", "Ciao {NOME}, la password è {PASSWORD}"));
        var report = await RunAsync(s => s.SaveAsync(EmailTemplateKind.Report, "Referto", "Ciao {NOME} {COGNOME}"));

        Assert.True(password.Succeeded, password.Error);
        Assert.True(report.Succeeded, report.Error);
        var all = (await RunAsync(s => s.GetAllAsync())).Value!;
        Assert.Equal("Password per {COGNOME}", all.Single(t => t.Kind == EmailTemplateKind.Password).Subject);

        using var scope = _env.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().EmailTemplates.AsNoTracking().ToListAsync();
        Assert.All(stored, t => Assert.Equal(_admin.Id, t.UpdatedByUserId));
    }

    [Fact]
    public async Task Seeded_templates_are_valid_and_one_per_kind()
    {
        var all = (await RunAsync(s => s.GetAllAsync())).Value!;

        Assert.Equal(2, all.Count);
        foreach (var template in all)
        {
            Assert.Null(EmailTemplateService.Validate(template.Kind, template.Subject, template.Body));
        }
    }

    [Theory]
    [InlineData(Roles.Operatore)]
    [InlineData(Roles.Manager)]
    public async Task Only_Admin_and_SuperAdmin_can_read_or_save_templates(string role)
    {
        var user = await _env.AddUserAsync("altro", role);
        _env.CurrentUser.UserId = user.Id;

        Assert.False((await RunAsync(s => s.GetAllAsync())).Succeeded);
        Assert.False((await RunAsync(s => s.SaveAsync(EmailTemplateKind.Report, "Referto", "Ciao"))).Succeeded);
    }

    [Fact]
    public async Task SuperAdmin_can_save_templates()
    {
        var superAdmin = await _env.AddUserAsync("assistenza", Roles.SuperAdmin);
        _env.CurrentUser.UserId = superAdmin.Id;

        Assert.True((await RunAsync(s => s.SaveAsync(EmailTemplateKind.Report, "Referto", "Ciao"))).Succeeded);
    }

    [Fact]
    public void Preview_uses_sample_data_and_PASSWORD_is_offered_only_for_the_password_template()
    {
        var (subject, body) = EmailTemplateService.Preview("Per {COGNOME}", "Ciao {NOME}, {PASSWORD}");

        Assert.Equal("Per Rossi", subject);
        Assert.Equal("Ciao Mario, Ab3k9Qm2Xz7p", body);
        Assert.DoesNotContain("{PASSWORD}", EmailTemplateService.PlaceholdersFor(EmailTemplateKind.Report));
        Assert.Contains("{PASSWORD}", EmailTemplateService.PlaceholdersFor(EmailTemplateKind.Password));
    }
}
