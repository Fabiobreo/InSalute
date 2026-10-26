using Bunit;
using Bunit.TestDoubles;
using InSalute.Web.Components.Layout;
using Microsoft.AspNetCore.Components;
using MudBlazor.Services;
using Xunit;

namespace InSalute.Tests;

public class LayoutTests
{
    [Fact]
    public async Task MainLayout_shows_navigation_for_every_page_of_the_app()
    {
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();
        ctx.AddAuthorization().SetAuthorized("mario");

        var cut = ctx.Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "contenuto"))));

        foreach (var label in new[] { "Invia referto", "Storico invii", "Utenti", "SMTP", "Modelli email" })
        {
            Assert.Contains(label, cut.Markup);
        }
        Assert.Contains("contenuto", cut.Markup);
        Assert.Contains($"© {DateTime.Now.Year}", cut.Markup);
    }
}
