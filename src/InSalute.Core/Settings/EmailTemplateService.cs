using InSalute.Core.Data;
using InSalute.Core.Dispatching;
using InSalute.Core.Domain;
using InSalute.Core.Security;
using InSalute.Core.Texts;
using InSalute.Core.Users;
using Microsoft.EntityFrameworkCore;

namespace InSalute.Core.Settings;

public sealed record EmailTemplateView(EmailTemplateKind Kind, string Subject, string Body);

public sealed class EmailTemplateService(AppDbContext db, ActorProvider actors)
{
    public const int MaxSubjectLength = 200;
    public const string SampleFirstName = "Mario";
    public const string SampleLastName = "Rossi";
    public const string SamplePassword = "Ab3k9Qm2Xz7p";

    /// <summary>Segnaposto disponibili per il tipo di modello ({PASSWORD} solo in quello della password).</summary>
    public static IReadOnlyList<string> PlaceholdersFor(EmailTemplateKind kind) =>
        kind == EmailTemplateKind.Password
            ? TemplateRenderer.KnownPlaceholders
            : TemplateRenderer.KnownPlaceholders.Where(p => p != TemplateRenderer.Password).ToList();

    public async Task<OperationResult<IReadOnlyList<EmailTemplateView>>> GetAllAsync(CancellationToken ct = default)
    {
        if (await actors.GetAllowedAsync(Policies.CanManageSettings, ct) is null)
        {
            return OperationResult<IReadOnlyList<EmailTemplateView>>.Fail(Messaggi.NonAutorizzato);
        }

        var templates = await db.EmailTemplates.AsNoTracking()
            .OrderBy(t => t.Kind)
            .Select(t => new EmailTemplateView(t.Kind, t.Subject, t.Body))
            .ToListAsync(ct);
        return OperationResult<IReadOnlyList<EmailTemplateView>>.Ok(templates);
    }

    public async Task<OperationResult> SaveAsync(EmailTemplateKind kind, string subject, string body, CancellationToken ct = default)
    {
        var actor = await actors.GetAllowedAsync(Policies.CanManageSettings, ct);
        if (actor is null)
        {
            return OperationResult.Fail(Messaggi.NonAutorizzato);
        }

        var invalid = Validate(kind, subject, body);
        if (invalid is not null)
        {
            return OperationResult.Fail(invalid);
        }

        var template = await db.EmailTemplates.FirstOrDefaultAsync(t => t.Kind == kind, ct);
        if (template is null)
        {
            return OperationResult.Fail(Messaggi.ModelloNonTrovato);
        }

        template.Subject = subject.Trim();
        template.Body = body.Trim();
        template.UpdatedAt = DateTime.UtcNow;
        template.UpdatedByUserId = actor.User.Id;
        await db.SaveChangesAsync(ct);
        return OperationResult.Ok();
    }

    /// <summary>Anteprima con i dati di esempio. Non richiede permessi: non legge né scrive nulla.</summary>
    public static (string Subject, string Body) Preview(string subject, string body)
    {
        var values = new Dictionary<string, string>
        {
            [TemplateRenderer.FirstName] = SampleFirstName,
            [TemplateRenderer.LastName] = SampleLastName,
            [TemplateRenderer.Password] = SamplePassword,
        };
        var renderedSubject = TemplateRenderer.Render(subject, values);
        var renderedBody = TemplateRenderer.Render(body, values);
        // Con un modello non valido l'anteprima mostra il testo com'è: l'errore compare al salvataggio.
        return (renderedSubject.Succeeded ? renderedSubject.Text : subject, renderedBody.Succeeded ? renderedBody.Text : body);
    }

    /// <summary>Regole di 6.5. Restituisce il messaggio del primo problema, o null se il modello è valido.</summary>
    public static string? Validate(EmailTemplateKind kind, string? subject, string? body)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
        {
            return Messaggi.ModelloVuoto;
        }
        if (subject.Trim().Length > MaxSubjectLength)
        {
            return Messaggi.ModelloOggettoTroppoLungo;
        }

        var unknown = TemplateRenderer.FindUnknownPlaceholders(subject)
            .Concat(TemplateRenderer.FindUnknownPlaceholders(body))
            .FirstOrDefault();
        if (unknown is not null)
        {
            return Messaggi.SegnapostoNonRiconosciuto(unknown);
        }

        return kind switch
        {
            EmailTemplateKind.Password when !body.Contains(TemplateRenderer.Password, StringComparison.Ordinal)
                => Messaggi.ModelloPasswordSenzaSegnaposto,
            EmailTemplateKind.Report when subject.Contains(TemplateRenderer.Password, StringComparison.Ordinal)
                                          || body.Contains(TemplateRenderer.Password, StringComparison.Ordinal)
                => Messaggi.ModelloRefertoConPassword,
            _ => null,
        };
    }
}
