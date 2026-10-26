using System.Text.RegularExpressions;
using InSalute.Core.Texts;

namespace InSalute.Core.Dispatching;

public sealed record RenderResult(bool Succeeded, string Text = "", string? Error = null);

/// <summary>Sostituisce {NOME}, {COGNOME}, {PASSWORD} (maiuscole esatte) e nient'altro.</summary>
public static partial class TemplateRenderer
{
    public const string FirstName = "{NOME}";
    public const string LastName = "{COGNOME}";
    public const string Password = "{PASSWORD}";

    public static readonly IReadOnlyList<string> KnownPlaceholders = [FirstName, LastName, Password];

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex PlaceholderPattern();

    /// <summary>Segnaposto presenti nel testo che non fanno parte di quelli previsti, nell'ordine in cui compaiono.</summary>
    public static IReadOnlyList<string> FindUnknownPlaceholders(string text) =>
        PlaceholderPattern().Matches(text)
            .Select(m => m.Value)
            .Where(v => !KnownPlaceholders.Contains(v))
            .Distinct()
            .ToList();

    /// <summary>
    /// Sostituisce in un solo passaggio i segnaposto di cui è fornito il valore (il valore inserito non viene riesaminato).
    /// Se dopo la sostituzione resta un {...} restituisce un errore.
    /// </summary>
    public static RenderResult Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var text = PlaceholderPattern().Replace(
            template,
            m => values.TryGetValue(m.Value, out var value) ? value.Trim() : m.Value);

        // Nome e cognome sono inseriti come scritti dall'operatore: se contengono {…} è meglio fermarsi che spedire il testo.
        var leftover = PlaceholderPattern().Match(text);
        return leftover.Success
            ? new RenderResult(false, Error: Messaggi.SegnapostoRimasto(leftover.Value))
            : new RenderResult(true, text);
    }
}
