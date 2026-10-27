using Bunit;
using Bunit.TestDoubles;
using InSalute.Core.Data;
using InSalute.Core.Dispatching;
using InSalute.Core.Domain;
using InSalute.Core.Security;
using InSalute.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace InSalute.Tests;

public sealed class StoricoPageTests : IAsyncLifetime
{
    private readonly TestEnvironment _env = new();
    private readonly BunitContext _ctx = new();
    private IServiceScope _scope = default!;
    private ApplicationUser _admin = default!;
    private ApplicationUser _super = default!;

    public async Task InitializeAsync()
    {
        _admin = await _env.AddUserAsync("admin", Roles.Admin);
        _super = await _env.AddUserAsync("assistenza", Roles.SuperAdmin);
        var op = await _env.AddUserAsync("operatore", Roles.Operatore);

        using var scope = _env.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Dispatches.AddRange(
            NewDispatch(op, "Rossi", DispatchStepStatus.Sent, DispatchStepStatus.Sent),
            NewDispatch(_super, "Bianchi", DispatchStepStatus.Sent, DispatchStepStatus.Abandoned));
        await db.SaveChangesAsync();
    }

    private static Dispatch NewDispatch(ApplicationUser by, string last, DispatchStepStatus report, DispatchStepStatus password) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow.AddMinutes(-5),
        CreatedByUserId = by.Id,
        PatientFirstName = "Mario",
        PatientLastName = last,
        RecipientEmail = last.ToLowerInvariant() + "@example.test",
        AttachmentsJson = "[]",
        ReportStatus = report,
        PasswordStatus = password,
    };

    public async Task DisposeAsync()
    {
        await _ctx.DisposeAsync();
        _scope?.Dispose();
        await _env.DisposeAsync();
    }

    private void Setup(ApplicationUser viewer)
    {
        _env.CurrentUser.UserId = viewer.Id;
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddMudServices();
        _ctx.AddAuthorization().SetAuthorized(viewer.UserName!).SetPolicies(Policies.CanSend);
        _scope = _env.CreateScope();
        _ctx.Services.AddSingleton(_scope.ServiceProvider.GetRequiredService<DispatchHistoryService>());
        _ctx.Services.AddSingleton(_scope.ServiceProvider.GetRequiredService<TimeProvider>());
        _ctx.Render<MudPopoverProvider>();
    }

    [Fact]
    public void The_grid_lists_all_dispatches_with_status_chips_and_is_read_only()
    {
        Setup(_admin);

        var cut = _ctx.Render<Storico>();

        cut.WaitForAssertion(() => Assert.Contains("Rossi Mario", cut.Markup));
        Assert.Contains("Bianchi Mario", cut.Markup);
        Assert.Contains("Completato", cut.Markup);
        Assert.Contains("Password non inviata", cut.Markup);
        // Nella tabella non c'è nessun campo da modificare.
        Assert.Empty(cut.FindAll("table input, table textarea, table select"));
    }

    [Fact]
    public void An_admin_sees_technical_support_instead_of_the_super_admin_name()
    {
        Setup(_admin);

        var cut = _ctx.Render<Storico>();

        cut.WaitForAssertion(() => Assert.Contains("Bianchi", cut.Markup));
        Assert.Contains(DispatchHistoryService.TechnicalSupportName, cut.Markup);
        Assert.DoesNotContain(">assistenza<", cut.Markup);
    }

    [Fact]
    public void A_super_admin_sees_their_own_name()
    {
        Setup(_super);

        var cut = _ctx.Render<Storico>();

        cut.WaitForAssertion(() => Assert.Contains("Bianchi", cut.Markup));
        Assert.Contains(">assistenza<", cut.Markup);
    }

    [Fact]
    public async Task The_detail_page_shows_the_password_not_sent_note()
    {
        Setup(_admin);
        Guid id;
        using (var scope = _env.CreateScope())
        {
            id = scope.ServiceProvider.GetRequiredService<AppDbContext>().Dispatches
                .Single(d => d.PatientLastName == "Bianchi").Id;
        }

        var cut = _ctx.Render<DettaglioInvio>(p => p.Add(x => x.Id, id));

        cut.WaitForAssertion(() => Assert.Contains("Bianchi Mario", cut.Markup));
        Assert.Contains("Rifare l'invio completo", cut.Find(".mud-alert").TextContent);
        Assert.Contains(DispatchHistoryService.TechnicalSupportName, cut.Markup);
        await Task.CompletedTask;
    }
}
