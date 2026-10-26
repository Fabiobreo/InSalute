namespace InSalute.Core.Domain;

/// <summary>Un invio. La password del documento non è mai salvata: né qui, né altrove.</summary>
public class Dispatch
{
    public Guid Id { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string CreatedByUserId { get; set; } = "";

    public ApplicationUser? CreatedBy { get; set; }

    public string PatientFirstName { get; set; } = "";

    public string PatientLastName { get; set; } = "";

    /// <summary>In minuscolo e senza spazi.</summary>
    public string RecipientEmail { get; set; } = "";

    /// <summary>JSON: lista di { fileName, sizeBytes, sha256 } dei file originali caricati.</summary>
    public string AttachmentsJson { get; set; } = "[]";

    public string? InvoiceFileName { get; set; }

    public ProtectionKind ProtectionKind { get; set; }

    public DispatchStepStatus ReportStatus { get; set; } = DispatchStepStatus.Pending;

    public DateTime? ReportSentAt { get; set; }

    /// <summary>Messaggio tecnico dell'errore, senza dati del paziente.</summary>
    public string? ReportError { get; set; }

    public DispatchStepStatus PasswordStatus { get; set; } = DispatchStepStatus.Pending;

    public DateTime? PasswordSentAt { get; set; }

    public string? PasswordError { get; set; }

    public bool IsLegacyImport { get; set; }
}
