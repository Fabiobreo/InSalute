using InSalute.Core.Data;
using InSalute.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InSalute.Core.Dispatching;

public sealed record CleanupSummary(int FoldersDeleted, int DispatchesAbandoned, int DispatchesDeleted);

/// <summary>
/// Pulizia periodica (7.5): cartelle temporanee più vecchie di 60 minuti e invii rimasti a metà.
/// Con <see cref="RetentionOptions.DispatchYears"/> impostato cancella anche lo storico più vecchio; di default non cancella mai.
/// </summary>
public sealed class DispatchCleanupService(
    AppDbContext db,
    TempFileStore tempFiles,
    IOptions<RetentionOptions> retention,
    TimeProvider time,
    ILogger<DispatchCleanupService> logger)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(60);

    public async Task<CleanupSummary> RunAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;

        var folders = tempFiles.DeleteOlderThan(MaxAge, now);

        var cutoff = now - MaxAge;
        var stale = await db.Dispatches
            .Where(d => d.CreatedAt < cutoff
                && (d.ReportStatus == DispatchStepStatus.Pending || d.ReportStatus == DispatchStepStatus.Failed
                    || d.PasswordStatus == DispatchStepStatus.Pending || d.PasswordStatus == DispatchStepStatus.Failed))
            .ToListAsync(ct);
        foreach (var dispatch in stale)
        {
            if (dispatch.ReportStatus is DispatchStepStatus.Pending or DispatchStepStatus.Failed)
            {
                dispatch.ReportStatus = DispatchStepStatus.Abandoned;
            }
            if (dispatch.PasswordStatus is DispatchStepStatus.Pending or DispatchStepStatus.Failed)
            {
                dispatch.PasswordStatus = DispatchStepStatus.Abandoned;
            }
        }
        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        var deleted = 0;
        if (retention.Value.DispatchYears is { } years and > 0)
        {
            var retentionCutoff = now.AddYears(-years);
            deleted = await db.Dispatches.Where(d => d.CreatedAt < retentionCutoff).ExecuteDeleteAsync(ct);
        }

        if (folders > 0 || stale.Count > 0 || deleted > 0)
        {
            logger.LogInformation(
                "Pulizia: {Folders} cartelle temporanee, {Abandoned} invii abbandonati, {Deleted} invii scaduti cancellati",
                folders, stale.Count, deleted);
        }
        return new CleanupSummary(folders, stale.Count, deleted);
    }
}
