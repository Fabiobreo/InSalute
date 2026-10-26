using System.Globalization;
using System.Text;
using InSalute.Core.Data;
using InSalute.Core.Security;
using InSalute.Core.Texts;
using InSalute.Core.Users;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace InSalute.Core.Dispatching;

public sealed record DomainSuggestion(string Domain, string Email);

/// <summary>Controlli sul destinatario e sui nomi dei file (6.1, passi 1 e 2). Sono tutti avvisi: nessuno blocca l'invio.</summary>
public sealed class RecipientChecks(AppDbContext db, ActorProvider actors)
{
    public static readonly IReadOnlyList<string> CommonDomains =
    [
        "gmail.com", "hotmail.it", "hotmail.com", "outlook.it", "outlook.com", "live.it", "libero.it",
        "yahoo.it", "yahoo.com", "icloud.com", "virgilio.it", "alice.it", "tiscali.it", "tim.it", "pec.it",
    ];

    /// <summary>Formato valido: indirizzo semplice (senza nome visualizzato) con almeno un punto nel dominio.</summary>
    public static bool IsValidEmail(string? email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !MailboxAddress.TryParse(trimmed, out var mailbox))
        {
            return false;
        }

        var at = mailbox.Address.LastIndexOf('@');
        return at > 0
            && mailbox.Address.Equals(trimmed, StringComparison.Ordinal)
            && mailbox.Address[(at + 1)..].Contains('.');
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>
    /// Se il dominio dista 1-2 caratteri (Levenshtein) da uno comune e non coincide esattamente con nessuno, lo suggerisce.
    /// </summary>
    public static DomainSuggestion? SuggestDomain(string email)
    {
        var normalized = NormalizeEmail(email);
        var at = normalized.LastIndexOf('@');
        if (at <= 0 || at == normalized.Length - 1)
        {
            return null;
        }

        var domain = normalized[(at + 1)..];
        if (CommonDomains.Contains(domain))
        {
            return null;
        }

        var best = CommonDomains
            .Select(d => (Domain: d, Distance: Levenshtein(domain, d)))
            .Where(x => x.Distance is >= 1 and <= 2)
            .OrderBy(x => x.Distance)
            .FirstOrDefault();
        return best.Domain is null ? null : new DomainSuggestion(best.Domain, normalized[..at] + "@" + best.Domain);
    }

    /// <summary>
    /// Vero se nello storico esiste un invio a questo indirizzo con un cognome diverso.
    /// Non dice mai quale altro paziente: solo se esiste. Gli invii importati (senza cognome) non contano.
    /// </summary>
    public async Task<OperationResult<bool>> IsUsedForAnotherPatientAsync(string email, string lastName, CancellationToken ct = default)
    {
        if (await actors.GetAllowedAsync(Policies.CanSend, ct) is null)
        {
            return OperationResult<bool>.Fail(Messaggi.NonAutorizzato);
        }

        var recipient = NormalizeEmail(email);
        var lastNames = await db.Dispatches.AsNoTracking()
            .Where(d => d.RecipientEmail == recipient && !d.IsLegacyImport && d.PatientLastName != "")
            .Select(d => d.PatientLastName)
            .Distinct()
            .ToListAsync(ct);

        var current = NormalizeForMatch(lastName);
        return OperationResult<bool>.Ok(lastNames.Any(n => NormalizeForMatch(n) != current));
    }

    /// <summary>Vero se nessun nome di file contiene il cognome (senza maiuscole, accenti e spazi).</summary>
    public static bool NoFileNameContainsLastName(string lastName, IEnumerable<string> fileNames)
    {
        var needle = NormalizeForMatch(lastName);
        return needle.Length > 0 && !fileNames.Any(f => NormalizeForMatch(f).Contains(needle, StringComparison.Ordinal));
    }

    /// <summary>Minuscolo, senza accenti, solo lettere e cifre.</summary>
    public static string NormalizeForMatch(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }
        return builder.ToString();
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
