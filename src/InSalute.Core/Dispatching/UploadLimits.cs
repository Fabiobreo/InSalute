namespace InSalute.Core.Dispatching;

/// <summary>Limiti dei documenti caricati (6.1, passo 2).</summary>
public static class UploadLimits
{
    public const int MaxReports = 10;
    public const long MaxFileBytes = 20L * 1024 * 1024;
    public const long MaxTotalBytes = 50L * 1024 * 1024;

    public static readonly IReadOnlyList<string> AllowedExtensions = [".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx", ".odt", ".txt"];

    /// <summary>Confronto senza differenza tra maiuscole e minuscole (il vecchio sistema falliva con .PDF).</summary>
    public static bool IsAllowed(string fileName) =>
        AllowedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
}
