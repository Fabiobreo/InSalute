using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Bunit;
using Bunit.TestDoubles;
using InSalute.Core.Security;
using InSalute.Web.Components.Layout;
using InSalute.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace InSalute.Tests;

/// <summary>Nessun testo in inglese davanti agli utenti: né quelli interni di MudBlazor, né il dialogo di riconnessione.</summary>
public sealed partial class LocalizationTests
{
    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    private static Dictionary<string, string> MudBlazorEnglishTexts()
    {
        var resources = new ResourceManager("MudBlazor.Resources.LanguageResource", typeof(MudLocalizer).Assembly);
        var set = resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)!;
        return set.Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    [Fact]
    public void Every_MudBlazor_text_has_an_italian_translation_with_the_same_placeholders()
    {
        var english = MudBlazorEnglishTexts();

        // Se un aggiornamento di MudBlazor aggiunge testi, questo test elenca quelli da tradurre.
        Assert.Empty(english.Keys.Except(ItalianMudLocalizer.Texts.Keys).Order());
        Assert.Empty(ItalianMudLocalizer.Texts.Keys.Except(english.Keys).Order());
        foreach (var (key, text) in english)
        {
            Assert.True(
                Placeholder().Matches(text).Select(m => m.Value).Order().SequenceEqual(
                    Placeholder().Matches(ItalianMudLocalizer.Texts[key]).Select(m => m.Value).Order()),
                $"Segnaposto diversi in {key}");
        }
    }

    [Fact]
    public void Texts_with_arguments_are_formatted()
    {
        var localizer = new ItalianMudLocalizer();

        Assert.Equal("Apri o chiudi Impostazioni", localizer["MudNavGroup_ToggleExpand", "Impostazioni"].Value);
        Assert.Equal("Righe per pagina:", localizer["MudDataGridPager_RowsPerPage"].Value);
    }

    [Fact]
    public async Task The_layout_uses_italian_texts_for_MudBlazor_components()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("it-IT");
        try
        {
            await using var ctx = new BunitContext();
            ctx.JSInterop.Mode = JSRuntimeMode.Loose;
            ctx.Services.AddMudServices();
            ctx.Services.AddTransient<MudLocalizer, ItalianMudLocalizer>();
            ctx.AddAuthorization().SetAuthorized("mario").SetPolicies(Policies.CanSend, Policies.CanManageSettings);

            var cut = ctx.Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "contenuto"))));

            Assert.Contains("Apri o chiudi Impostazioni", cut.Markup);
            Assert.DoesNotContain("Toggle", cut.Markup);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task The_reconnection_dialog_is_in_italian()
    {
        await using var ctx = new BunitContext();

        var cut = ctx.Render<ReconnectModal>();

        Assert.Contains("Riconnessione in corso", cut.Markup);
        Assert.Equal(["Riprova", "Riprendi"], cut.FindAll("button").Select(b => b.TextContent.Trim()));
        // Solo il testo visibile: le classi CSS di Blazor ("components-reconnect-failed-visible") restano in inglese.
        var text = cut.Find("dialog").TextContent;
        foreach (var english in new[] { "Rejoin", "Retry", "Resume", "Failed", "Please", "paused", "server..." })
        {
            Assert.DoesNotContain(english, text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
