using System.Globalization;

namespace InSalute.Web.Services;

public static class FileSizeFormat
{
    private static readonly CultureInfo Italian = new("it-IT");

    /// <summary>Dimensione leggibile, per esempio "1,2 MB".</summary>
    public static string ToText(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(Italian, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(Italian, $"{bytes / (1024.0 * 1024.0):0.#} MB"),
    };
}
