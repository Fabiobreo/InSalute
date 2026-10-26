namespace InSalute.Core.Domain;

public class EmailTemplate
{
    public int Id { get; set; }

    /// <summary>Un solo record per tipo (indice univoco).</summary>
    public EmailTemplateKind Kind { get; set; }

    public string Subject { get; set; } = "";

    /// <summary>Testo semplice (non HTML), può contenere segnaposto.</summary>
    public string Body { get; set; } = "";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string? UpdatedByUserId { get; set; }
}
