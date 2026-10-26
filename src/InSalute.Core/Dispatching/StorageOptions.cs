namespace InSalute.Core.Dispatching;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Cartella dei file temporanei, fuori dalla cartella dell'applicazione.</summary>
    public string TempPath { get; set; } = @"C:\InSaluteData\tmp";
}

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>Anni di conservazione dello storico. Null = mai cancellare (nessun termine stabilito dal centro).</summary>
    public int? DispatchYears { get; set; }
}
