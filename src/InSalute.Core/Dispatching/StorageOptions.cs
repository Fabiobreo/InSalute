namespace InSalute.Core.Dispatching;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Cartella dei file temporanei, fuori dalla cartella dell'applicazione.</summary>
    public string TempPath { get; set; } = @"C:\InSaluteData\tmp";

    /// <summary>
    /// Tempo massimo senza ricevere dati durante un caricamento. Se la connessione col browser cade a metà,
    /// la lettura non finisce mai da sola: dopo questo tempo il file viene rifiutato.
    /// </summary>
    public TimeSpan UploadIdleTimeout { get; set; } = TimeSpan.FromSeconds(60);
}

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>Anni di conservazione dello storico. Null = mai cancellare (nessun termine stabilito dal centro).</summary>
    public int? DispatchYears { get; set; }
}
