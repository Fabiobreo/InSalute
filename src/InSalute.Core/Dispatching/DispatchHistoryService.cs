using System.Text.Json;
using InSalute.Core.Data;
using InSalute.Core.Domain;
using InSalute.Core.Security;
using InSalute.Core.Texts;
using InSalute.Core.Users;
using Microsoft.EntityFrameworkCore;

namespace InSalute.Core.Dispatching;

/// <summary>Stato complessivo di un invio (4.3): calcolato, mai salvato.</summary>
public enum DispatchOverallStatus
{
    Completed,
    PasswordNotSent,
    NotSent,
    Imported,
}

public static class DispatchOverallStatusExtensions
{
    public static string Label(this DispatchOverallStatus status) => status switch
    {
        DispatchOverallStatus.Completed => "Completato",
        DispatchOverallStatus.PasswordNotSent => "Password non inviata",
        DispatchOverallStatus.NotSent => "Non inviato",
        _ => "Importato (esito sconosciuto)",
    };

    public static DispatchOverallStatus Of(Dispatch d) =>
        d.IsLegacyImport ? DispatchOverallStatus.Imported
        : d.ReportStatus != DispatchStepStatus.Sent ? DispatchOverallStatus.NotSent
        : d.PasswordStatus == DispatchStepStatus.Sent ? DispatchOverallStatus.Completed
        : DispatchOverallStatus.PasswordNotSent;
}

/// <param name="From">Primo giorno incluso, in ora italiana.</param>
/// <param name="To">Ultimo giorno incluso, in ora italiana.</param>
/// <param name="Text">Cerca in cognome, nome ed email del destinatario.</param>
public sealed record DispatchFilter(
    DateOnly? From = null,
    DateOnly? To = null,
    string? Text = null,
    DispatchOverallStatus? Status = null,
    string? OperatorId = null);

public sealed record DispatchListItem(
    Guid Id,
    DateTime CreatedAt,
    string Patient,
    string RecipientEmail,
    string OperatorName,
    DispatchOverallStatus Status);

public sealed record DispatchPage(IReadOnlyList<DispatchListItem> Items, int TotalCount);

public sealed record OperatorOption(string Id, string DisplayName);

public sealed record DispatchFileInfo(string FileName, long SizeBytes, string Sha256);

public sealed record DispatchDetail(
    Guid Id,
    DateTime CreatedAt,
    string PatientFirstName,
    string PatientLastName,
    string RecipientEmail,
    string OperatorName,
    DispatchOverallStatus Status,
    ProtectionKind ProtectionKind,
    IReadOnlyList<DispatchFileInfo> Files,
    string? InvoiceFileName,
    DispatchStepStatus ReportStatus,
    DateTime? ReportSentAt,
    string? ReportError,
    DispatchStepStatus PasswordStatus,
    DateTime? PasswordSentAt,
    string? PasswordError,
    bool IsLegacyImport);

/// <summary>
/// Storico degli invii in sola lettura. Tutti i ruoli vedono tutti gli invii; gli invii fatti da un SuperAdmin
/// mostrano "Assistenza tecnica" a chiunque non sia SuperAdmin. La maschera è applicata qui, non nella UI.
/// </summary>
public sealed class DispatchHistoryService(AppDbContext db, ActorProvider actors)
{
    public const string TechnicalSupportName = "Assistenza tecnica";

    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    public async Task<OperationResult<DispatchPage>> QueryAsync(
        DispatchFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        var actor = await actors.GetAllowedAsync(Policies.CanSend, ct);
        if (actor is null)
        {
            return OperationResult<DispatchPage>.Fail(Messaggi.NonAutorizzato);
        }
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var actorIsSuper = actor.Role == Roles.SuperAdmin;
        var superIds = await GetSuperAdminIdsAsync(ct);
        var query = db.Dispatches.AsNoTracking().AsQueryable();

        if (filter.From is { } from)
        {
            var fromUtc = ToUtcStartOfDay(from);
            query = query.Where(d => d.CreatedAt >= fromUtc);
        }
        if (filter.To is { } to)
        {
            var beforeUtc = ToUtcStartOfDay(to.AddDays(1));
            query = query.Where(d => d.CreatedAt < beforeUtc);
        }
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var text = filter.Text.Trim().ToLowerInvariant();
            query = query.Where(d =>
                d.PatientLastName.ToLower().Contains(text)
                || d.PatientFirstName.ToLower().Contains(text)
                || d.RecipientEmail.Contains(text));
        }
        if (filter.Status is { } status)
        {
            query = status switch
            {
                DispatchOverallStatus.Imported => query.Where(d => d.IsLegacyImport),
                DispatchOverallStatus.NotSent => query.Where(d => !d.IsLegacyImport && d.ReportStatus != DispatchStepStatus.Sent),
                DispatchOverallStatus.Completed => query.Where(d => !d.IsLegacyImport
                    && d.ReportStatus == DispatchStepStatus.Sent && d.PasswordStatus == DispatchStepStatus.Sent),
                _ => query.Where(d => !d.IsLegacyImport
                    && d.ReportStatus == DispatchStepStatus.Sent && d.PasswordStatus != DispatchStepStatus.Sent),
            };
        }
        if (!string.IsNullOrEmpty(filter.OperatorId))
        {
            var operatorId = filter.OperatorId;
            query = query.Where(d => d.CreatedByUserId == operatorId);
            if (!actorIsSuper)
            {
                // Filtrare per l'Id di un SuperAdmin non deve rivelare nulla: nessun risultato.
                query = query.Where(d => !superIds.Contains(d.CreatedByUserId));
            }
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id)
            .Skip(page * pageSize).Take(pageSize)
            .Select(d => new
            {
                d.Id,
                d.CreatedAt,
                d.PatientLastName,
                d.PatientFirstName,
                d.RecipientEmail,
                d.IsLegacyImport,
                d.ReportStatus,
                d.PasswordStatus,
                OperatorName = d.CreatedBy!.DisplayName,
                CreatorIsSuperAdmin = superIds.Contains(d.CreatedByUserId),
            })
            .ToListAsync(ct);

        IReadOnlyList<DispatchListItem> items = rows.Select(r => new DispatchListItem(
            r.Id,
            r.CreatedAt,
            PatientName(r.PatientLastName, r.PatientFirstName),
            r.RecipientEmail,
            r.CreatorIsSuperAdmin && !actorIsSuper ? TechnicalSupportName : r.OperatorName,
            DispatchOverallStatusExtensions.Of(new Dispatch
            {
                IsLegacyImport = r.IsLegacyImport,
                ReportStatus = r.ReportStatus,
                PasswordStatus = r.PasswordStatus,
            }))).ToList();
        return OperationResult<DispatchPage>.Ok(new DispatchPage(items, total));
    }

    /// <summary>Utenti selezionabili nel filtro operatore. Il SuperAdmin compare solo a un altro SuperAdmin.</summary>
    public async Task<OperationResult<IReadOnlyList<OperatorOption>>> GetOperatorsAsync(CancellationToken ct = default)
    {
        var actor = await actors.GetAllowedAsync(Policies.CanSend, ct);
        if (actor is null)
        {
            return OperationResult<IReadOnlyList<OperatorOption>>.Fail(Messaggi.NonAutorizzato);
        }

        var actorIsSuper = actor.Role == Roles.SuperAdmin;
        var superIds = await GetSuperAdminIdsAsync(ct);
        var options = await db.Users.AsNoTracking()
            .Where(u => actorIsSuper || !superIds.Contains(u.Id))
            .OrderBy(u => u.DisplayName)
            .Select(u => new OperatorOption(u.Id, u.DisplayName))
            .ToListAsync(ct);
        return OperationResult<IReadOnlyList<OperatorOption>>.Ok(options);
    }

    public async Task<OperationResult<DispatchDetail>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var actor = await actors.GetAllowedAsync(Policies.CanSend, ct);
        if (actor is null)
        {
            return OperationResult<DispatchDetail>.Fail(Messaggi.NonAutorizzato);
        }

        var superIds = await GetSuperAdminIdsAsync(ct);
        var row = await db.Dispatches.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new { Dispatch = d, OperatorName = d.CreatedBy!.DisplayName, CreatorIsSuperAdmin = superIds.Contains(d.CreatedByUserId) })
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return OperationResult<DispatchDetail>.Fail(Messaggi.InvioNonTrovato);
        }

        var d = row.Dispatch;
        var operatorName = row.CreatorIsSuperAdmin && actor.Role != Roles.SuperAdmin ? TechnicalSupportName : row.OperatorName;
        return OperationResult<DispatchDetail>.Ok(new DispatchDetail(
            d.Id, d.CreatedAt, d.PatientFirstName, d.PatientLastName, d.RecipientEmail, operatorName,
            DispatchOverallStatusExtensions.Of(d), d.ProtectionKind, ParseFiles(d.AttachmentsJson), d.InvoiceFileName,
            d.ReportStatus, d.ReportSentAt, d.ReportError, d.PasswordStatus, d.PasswordSentAt, d.PasswordError, d.IsLegacyImport));
    }

    /// <summary>Pochi account: si leggono una volta e si usano nelle query come elenco.</summary>
    private Task<List<string>> GetSuperAdminIdsAsync(CancellationToken ct) =>
        (from ur in db.UserRoles.AsNoTracking()
         join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
         where r.Name == Roles.SuperAdmin
         select ur.UserId).ToListAsync(ct);

    private static string PatientName(string last, string first) =>
        (last + " " + first).Trim() is { Length: > 0 } name ? name : "—";

    private static DateTime ToUtcStartOfDay(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), Rome);

    private sealed record StoredAttachment(string FileName, long SizeBytes, string Sha256);

    private static IReadOnlyList<DispatchFileInfo> ParseFiles(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<StoredAttachment>>(json, JsonSerializerOptions.Web) ?? [])
                .Select(a => new DispatchFileInfo(a.FileName, a.SizeBytes, a.Sha256))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
