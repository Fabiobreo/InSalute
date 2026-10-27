using System.Security.Cryptography;
using System.Text;
using InSalute.Core.Data;
using InSalute.Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace InSalute.LegacyImport;

/// <summary>
/// Importa utenti e log dal vecchio sistema (piano, fase 7). Idempotente: gli utenti si riconoscono da
/// <c>LegacyId</c>, i Dispatch da un Id derivato dal primo log del gruppo.
/// </summary>
public sealed class LegacyImporter(AppDbContext db, UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles)
{
    /// <summary>Righe di log dello stesso utente e destinatario entro questo intervallo dalla prima del gruppo sono un solo invio.</summary>
    public static readonly TimeSpan GroupWindow = TimeSpan.FromMinutes(2);

    private const int MaxEmailLength = 254;
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    public static string? MapRole(string legacyRole) => legacyRole.Trim().ToLowerInvariant() switch
    {
        "admin" => Roles.Admin,
        "manager" => Roles.Manager,
        "user" => Roles.Operatore,
        _ => null,
    };

    /// <summary>Le date del vecchio DB sono in ora italiana, senza fuso: si convertono in UTC.</summary>
    public static DateTime ToUtc(DateTime italianLocal)
    {
        var unspecified = DateTime.SpecifyKind(italianLocal, DateTimeKind.Unspecified);
        if (Rome.IsInvalidTime(unspecified))
        {
            // Ora inesistente (salto dell'ora legale): si sposta in avanti di un'ora.
            unspecified = unspecified.AddHours(1);
        }
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Rome);
    }

    /// <summary>Id stabile: rieseguire l'import sugli stessi dati produce gli stessi Id.</summary>
    public static Guid DispatchIdFor(long firstLogId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"insalute-legacy-dispatch:{firstLogId}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    public async Task<ImportSummary> ImportAsync(LegacyData data, bool dryRun, CancellationToken ct = default)
    {
        if (!dryRun)
        {
            foreach (var role in Roles.All)
            {
                if (!await roles.RoleExistsAsync(role))
                {
                    var created = await roles.CreateAsync(new IdentityRole(role));
                    if (!created.Succeeded)
                    {
                        throw new InvalidOperationException("Impossibile creare i ruoli nel database di destinazione.");
                    }
                }
            }
        }

        // ---- utenti ----
        var known = await db.Users.AsNoTracking()
            .Where(u => u.LegacyId != null)
            .ToDictionaryAsync(u => u.LegacyId!.Value, u => u.Id, ct);
        var userIds = new Dictionary<long, string>(known);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int alreadyImported = 0, toCreate = 0, skipped = 0;

        foreach (var legacy in data.Users)
        {
            if (known.ContainsKey(legacy.Id))
            {
                alreadyImported++;
                continue;
            }

            var role = MapRole(legacy.Role);
            var userName = legacy.UserName.Trim();
            var email = legacy.Email.Trim();
            if (role is null || userName.Length == 0 || email.Length == 0)
            {
                skipped++;
                continue;
            }

            if (dryRun)
            {
                // Come nell'import vero: nome utente o email già presenti (nel nuovo DB o in questa stessa lettura) bloccano l'utente.
                if (await users.FindByNameAsync(userName) is not null
                    || await users.FindByEmailAsync(email) is not null
                    || !seenNames.Add(userName)
                    || !seenEmails.Add(email))
                {
                    skipped++;
                    continue;
                }
                toCreate++;
                userIds[legacy.Id] = $"dry-run:{legacy.Id}";
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = userName,
                Email = email,
                DisplayName = userName,
                CreatedAt = ToUtc(legacy.CreationDate),
                LegacyId = legacy.Id,
                // Nessuna password migrata: un Admin riattiva l'utente e gli assegna una password temporanea.
                IsActive = false,
                MustChangePassword = true,
                LockoutEnabled = true,
            };
            var result = await users.CreateAsync(user);
            if (!result.Succeeded)
            {
                // Per esempio nome utente o email duplicati nel vecchio DB.
                skipped++;
                continue;
            }
            if (!(await users.AddToRoleAsync(user, role)).Succeeded)
            {
                await users.DeleteAsync(user);
                skipped++;
                continue;
            }
            toCreate++;
            userIds[legacy.Id] = user.Id;
        }

        // ---- log -> Dispatch ----
        var existingIds = (await db.Dispatches.AsNoTracking().Where(d => d.IsLegacyImport).Select(d => d.Id).ToListAsync(ct)).ToHashSet();
        int logSkipped = 0, resulting = 0, dispatchesPresent = 0, dispatchesNew = 0;
        var dispatches = new List<Dispatch>();

        var usable = new List<(LegacyLog Log, string Email)>();
        foreach (var log in data.Logs)
        {
            var email = log.ReceiverEmail.Trim().ToLowerInvariant();
            if (!userIds.ContainsKey(log.UserId) || email.Length == 0 || email.Length > MaxEmailLength)
            {
                logSkipped++;
                continue;
            }
            usable.Add((log, email));
        }

        foreach (var perRecipient in usable.GroupBy(x => (x.Log.UserId, x.Email)))
        {
            var rows = perRecipient.OrderBy(x => x.Log.SendingTime).ThenBy(x => x.Log.Id).ToList();
            var start = 0;
            while (start < rows.Count)
            {
                var end = start + 1;
                while (end < rows.Count && rows[end].Log.SendingTime - rows[start].Log.SendingTime < GroupWindow)
                {
                    end++;
                }

                var first = rows[start..end].MinBy(x => x.Log.Id).Log;
                var id = DispatchIdFor(first.Id);
                resulting++;
                if (existingIds.Contains(id))
                {
                    dispatchesPresent++;
                }
                else
                {
                    dispatchesNew++;
                    dispatches.Add(new Dispatch
                    {
                        Id = id,
                        CreatedAt = ToUtc(rows[start].Log.SendingTime),
                        CreatedByUserId = userIds[perRecipient.Key.UserId],
                        PatientFirstName = "",
                        PatientLastName = "",
                        RecipientEmail = perRecipient.Key.Email,
                        AttachmentsJson = "[]",
                        ProtectionKind = ProtectionKind.Unknown,
                        ReportStatus = DispatchStepStatus.Unknown,
                        PasswordStatus = DispatchStepStatus.Unknown,
                        IsLegacyImport = true,
                    });
                }
                start = end;
            }
        }

        if (!dryRun && dispatches.Count > 0)
        {
            db.Dispatches.AddRange(dispatches);
            await db.SaveChangesAsync(ct);
        }

        return new ImportSummary(
            data.Users.Count, alreadyImported, toCreate, skipped,
            data.Logs.Count, logSkipped, resulting, dispatchesPresent, dispatchesNew);
    }
}
