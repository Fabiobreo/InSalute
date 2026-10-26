using InSalute.Core.Domain;

namespace InSalute.Core.Dispatching;

/// <summary>Cosa serve per creare un invio. I file sono quelli già salvati in <see cref="TempFileStore"/> con lo stesso <see cref="DraftId"/>.</summary>
public sealed record DispatchRequest(
    Guid DraftId,
    string FirstName,
    string LastName,
    string RecipientEmail,
    IReadOnlyList<StoredFile> Reports,
    StoredFile? Invoice);

/// <summary>
/// Un invio preparato. La password del documento vive solo qui, in memoria (nel circuito Blazor): non è nel database,
/// nei file né nei log. Per questo è una classe senza <c>ToString</c> che la stampi.
/// </summary>
public sealed class PreparedDispatch
{
    public required Guid DispatchId { get; init; }

    public required string Password { get; init; }

    public required string ProtectedFilePath { get; init; }

    /// <summary>Nome del file che riceve il paziente.</summary>
    public required string FileName { get; init; }

    public required ProtectionKind ProtectionKind { get; init; }

    public string? InvoiceFilePath { get; init; }

    public string? InvoiceFileName { get; init; }

    public override string ToString() => $"PreparedDispatch {DispatchId}";
}

/// <summary>Esito di un tentativo di invio di una delle due email.</summary>
/// <param name="Attempted">Falso se non è partita nessuna email (permessi, stato già avanzato, file mancanti).</param>
/// <param name="UserMessage">Messaggio comprensibile da mostrare all'operatore, se qualcosa non è andato.</param>
/// <param name="TechnicalDetails">Solo per l'espandibile "Dettagli tecnici".</param>
public sealed record DispatchStepResult(bool Attempted, bool Succeeded, string? UserMessage = null, string? TechnicalDetails = null)
{
    public static DispatchStepResult NotAttempted(string? message = null) => new(false, false, message);
}
