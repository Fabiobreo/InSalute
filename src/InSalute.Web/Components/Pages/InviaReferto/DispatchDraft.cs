using InSalute.Core.Dispatching;

namespace InSalute.Web.Components.Pages.InviaReferto;

/// <summary>Un file della procedura: accettato (con <see cref="Stored"/>) oppure rifiutato con il motivo in <see cref="Error"/>.</summary>
public sealed class DraftFile
{
    public Guid Id { get; } = Guid.NewGuid();

    public required string Name { get; init; }

    public long SizeBytes { get; init; }

    public StoredFile? Stored { get; init; }

    public int? Pages { get; init; }

    public string? Error { get; init; }

    public bool IsAccepted => Error is null && Stored is not null;
}

/// <summary>
/// Stato della procedura guidata. Vive nel componente <c>InviaReferto</c> (nel circuito), mai in variabili statiche.
/// La password del documento sta solo dentro <see cref="Prepared"/>.
/// </summary>
public sealed class DispatchDraft
{
    public Guid DraftId { get; } = Guid.NewGuid();

    public string LastName { get; set; } = "";

    public string FirstName { get; set; } = "";

    public string Email { get; set; } = "";

    public string EmailConfirm { get; set; } = "";

    public List<DraftFile> Reports { get; } = [];

    public DraftFile? Invoice { get; set; }

    public PreparedDispatch? Prepared { get; set; }

    /// <summary>Vero quando l'invio è concluso: entrambe le email partite oppure invio annullato.</summary>
    public bool Finished { get; set; }

    public IEnumerable<DraftFile> AcceptedReports => Reports.Where(r => r.IsAccepted);
}
