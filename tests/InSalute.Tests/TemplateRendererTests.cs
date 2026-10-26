using InSalute.Core.Dispatching;
using Xunit;

namespace InSalute.Tests;

public class TemplateRendererTests
{
    private static readonly Dictionary<string, string> Values = new()
    {
        ["{NOME}"] = "  Mario ",
        ["{COGNOME}"] = "Rossi",
        ["{PASSWORD}"] = "Ab3k9Qm2Xz7p",
    };

    [Fact]
    public void Replaces_every_known_placeholder_with_trimmed_values()
    {
        var result = TemplateRenderer.Render("Buongiorno {NOME} {COGNOME}, password {PASSWORD}. {NOME}!", Values);

        Assert.True(result.Succeeded);
        Assert.Equal("Buongiorno Mario Rossi, password Ab3k9Qm2Xz7p. Mario!", result.Text);
    }

    [Fact]
    public void Placeholders_are_case_sensitive_so_a_lowercase_one_is_left_and_reported()
    {
        var result = TemplateRenderer.Render("Buongiorno {nome}", Values);

        Assert.False(result.Succeeded);
        Assert.Contains("{nome}", result.Error);
    }

    [Fact]
    public void A_placeholder_left_after_substitution_is_an_error()
    {
        var result = TemplateRenderer.Render("Ciao {NOME} {XYZ}", Values);

        Assert.False(result.Succeeded);
        Assert.Contains("{XYZ}", result.Error);
        Assert.Equal("", result.Text);
    }

    [Fact]
    public void A_value_is_not_substituted_again()
    {
        var values = new Dictionary<string, string> { ["{NOME}"] = "{COGNOME}", ["{COGNOME}"] = "Rossi" };

        var result = TemplateRenderer.Render("{NOME}", values);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void A_missing_value_leaves_the_placeholder_and_fails()
    {
        var result = TemplateRenderer.Render("Password: {PASSWORD}", new Dictionary<string, string> { ["{NOME}"] = "Mario" });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Finds_unknown_placeholders_only()
    {
        var unknown = TemplateRenderer.FindUnknownPlaceholders("{NOME} {XYZ} {COGNOME} {abc} {XYZ} {}");

        Assert.Equal(["{XYZ}", "{abc}", "{}"], unknown);
    }
}
