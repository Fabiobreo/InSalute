using Bunit;
using Bunit.TestDoubles;
using InSalute.Core.Security;
using InSalute.Web.Components.Layout;
using Microsoft.AspNetCore.Components;
using MudBlazor.Services;
using Xunit;

namespace InSalute.Tests;

public class LayoutTests
{
    private static IRenderedComponent<MainLayout> RenderLayout(BunitContext ctx, params string[] policies)
    {
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();
        ctx.AddAuthorization().SetAuthorized("mario").SetPolicies(policies);

        return ctx.Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "contenuto"))));
    }

    [Fact]
    public async Task MainLayout_shows_every_navigation_entry_to_an_admin()
    {
        await using var ctx = new BunitContext();

        var cut = RenderLayout(ctx, Policies.CanSend, Policies.CanManageUsers, Policies.CanManageSettings);

        foreach (var label in new[] { "Invia referto", "Storico invii", "Utenti", "SMTP", "Modelli email" })
        {
            Assert.Contains(label, cut.Markup);
        }
        Assert.Contains("contenuto", cut.Markup);
        Assert.Contains($"© {DateTime.Now.Year}", cut.Markup);
    }

    [Fact]
    public async Task MainLayout_hides_users_and_settings_from_an_operator()
    {
        await using var ctx = new BunitContext();

        var cut = RenderLayout(ctx, Policies.CanSend);

        Assert.Contains("Invia referto", cut.Markup);
        Assert.Contains("Storico invii", cut.Markup);
        Assert.DoesNotContain(">Utenti<", cut.Markup);
        Assert.DoesNotContain("SMTP", cut.Markup);
        Assert.DoesNotContain("Modelli email", cut.Markup);
    }

    [Fact]
    public async Task MainLayout_shows_users_but_not_settings_to_a_manager()
    {
        await using var ctx = new BunitContext();

        var cut = RenderLayout(ctx, Policies.CanSend, Policies.CanManageUsers);

        Assert.Contains("Utenti", cut.Markup);
        Assert.DoesNotContain("SMTP", cut.Markup);
    }
}
