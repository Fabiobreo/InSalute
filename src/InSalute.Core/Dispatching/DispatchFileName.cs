using System.Globalization;
using System.Text;

namespace InSalute.Core.Dispatching;

public static class DispatchFileName
{
    private const int MaxPartLength = 60;
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    /// <summary>Referto_{Cognome}_{Nome}_{yyyyMMdd}{extension}, con la data in ora italiana.</summary>
    public static string Build(string lastName, string firstName, DateTimeOffset now, string extension)
    {
        var date = TimeZoneInfo.ConvertTime(now, Rome).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return $"Referto_{Sanitize(lastName)}_{Sanitize(firstName)}_{date}{extension}";
    }

    /// <summary>Niente accenti (é→e), solo lettere, cifre e _; massimo 60 caratteri.</summary>
    public static string Sanitize(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        var result = builder.ToString().Trim('_');
        if (result.Length > MaxPartLength)
        {
            result = result[..MaxPartLength].TrimEnd('_');
        }
        return result.Length == 0 ? "Paziente" : result;
    }
}
