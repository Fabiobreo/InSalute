using InSalute.Core.Data;
using InSalute.Core.Domain;
using InSalute.Core.Email;
using InSalute.Core.Security;
using InSalute.Core.Texts;
using InSalute.Core.Users;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace InSalute.Core.Settings;

/// <summary>Impostazioni inviate dalla UI. <see cref="NewPassword"/> vuota = non cambiare la password.</summary>
public sealed record SmtpSettingsInput(
    string Host,
    int Port,
    SmtpSecurity Security,
    string UserName,
    string? NewPassword,
    string FromAddress,
    string FromName);

/// <summary>Impostazioni restituite alla UI: mai la password, solo se è impostata.</summary>
public sealed record SmtpSettingsView(
    string Host,
    int Port,
    SmtpSecurity Security,
    string UserName,
    bool HasPassword,
    string FromAddress,
    string FromName);

public sealed class SmtpSettingsService(
    AppDbContext db,
    ActorProvider actors,
    IDataProtectionProvider dataProtection,
    IEmailSender emailSender)
{
    public const string PasswordPurpose = "InSalute.SmtpPassword";

    /// <summary>Vero se le impostazioni sono state salvate almeno una volta. Non rivela alcun valore.</summary>
    public Task<bool> IsConfiguredAsync(CancellationToken ct = default) =>
        db.SmtpSettings.AsNoTracking().AnyAsync(s => s.Id == SmtpSettings.SingletonId, ct);

    /// <summary>Null se non ancora configurate; errore se l'utente non può gestire le impostazioni.</summary>
    public async Task<OperationResult<SmtpSettingsView?>> GetAsync(CancellationToken ct = default)
    {
        if (await actors.GetAllowedAsync(Policies.CanManageSettings, ct) is null)
        {
            return OperationResult<SmtpSettingsView?>.Fail(Messaggi.NonAutorizzato);
        }

        var view = await db.SmtpSettings.AsNoTracking()
            .Where(s => s.Id == SmtpSettings.SingletonId)
            .Select(s => new SmtpSettingsView(
                s.Host, s.Port, s.Security, s.UserName, s.ProtectedPassword != "", s.FromAddress, s.FromName))
            .FirstOrDefaultAsync(ct);
        return OperationResult<SmtpSettingsView?>.Ok(view);
    }

    public async Task<OperationResult> SaveAsync(SmtpSettingsInput input, CancellationToken ct = default)
    {
        var actor = await actors.GetAllowedAsync(Policies.CanManageSettings, ct);
        if (actor is null)
        {
            return OperationResult.Fail(Messaggi.NonAutorizzato);
        }

        var invalid = Validate(input);
        if (invalid is not null)
        {
            return OperationResult.Fail(invalid);
        }

        var settings = await db.SmtpSettings.FirstOrDefaultAsync(s => s.Id == SmtpSettings.SingletonId, ct);
        var isNew = settings is null;
        settings ??= new SmtpSettings();

        var userName = input.UserName.Trim();
        var hasNewPassword = !string.IsNullOrEmpty(input.NewPassword);
        if (userName.Length > 0 && !hasNewPassword && string.IsNullOrEmpty(settings.ProtectedPassword))
        {
            return OperationResult.Fail(Messaggi.PasswordSmtpObbligatoria);
        }

        settings.Host = input.Host.Trim();
        settings.Port = input.Port;
        settings.Security = input.Security;
        settings.UserName = userName;
        settings.FromAddress = input.FromAddress.Trim();
        settings.FromName = input.FromName.Trim();
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedByUserId = actor.User.Id;

        if (userName.Length == 0)
        {
            // Server senza autenticazione: nessuna password da conservare.
            settings.ProtectedPassword = "";
        }
        else if (hasNewPassword)
        {
            settings.ProtectedPassword = dataProtection.CreateProtector(PasswordPurpose).Protect(input.NewPassword!);
        }

        if (isNew)
        {
            db.SmtpSettings.Add(settings);
        }
        await db.SaveChangesAsync(ct);
        return OperationResult.Ok();
    }

    /// <summary>Invia "Email di prova InSalute" con le impostazioni salvate. L'esito dell'invio è nel valore restituito.</summary>
    public async Task<OperationResult<SendResult>> SendTestEmailAsync(string toAddress, CancellationToken ct = default)
    {
        if (await actors.GetAllowedAsync(Policies.CanManageSettings, ct) is null)
        {
            return OperationResult<SendResult>.Fail(Messaggi.NonAutorizzato);
        }
        if (!IsValidMailbox(toAddress))
        {
            return OperationResult<SendResult>.Fail(Messaggi.IndirizzoProvaNonValido);
        }
        if (!await IsConfiguredAsync(ct))
        {
            return OperationResult<SendResult>.Fail(Messaggi.SmtpNonConfigurato);
        }

        var result = await emailSender.SendAsync(
            new EmailMessage(toAddress.Trim(), Messaggi.OggettoEmailProva, Messaggi.CorpoEmailProva), ct);
        return OperationResult<SendResult>.Ok(result);
    }

    public static string? Validate(SmtpSettingsInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Host))
        {
            return Messaggi.HostObbligatorio;
        }
        if (input.Port is < 1 or > 65535)
        {
            return Messaggi.PortaNonValida;
        }
        if (!Enum.IsDefined(input.Security))
        {
            return Messaggi.SicurezzaNonValida;
        }
        if (!IsValidMailbox(input.FromAddress))
        {
            return Messaggi.MittenteNonValido;
        }
        if (input.FromName.Trim().Length > 100)
        {
            return Messaggi.NomeMittenteTroppoLungo;
        }
        return null;
    }

    private static bool IsValidMailbox(string? address) =>
        !string.IsNullOrWhiteSpace(address)
        && MailboxAddress.TryParse(address.Trim(), out var mailbox)
        && mailbox.Address.Contains('@')
        && mailbox.Address.Equals(address.Trim(), StringComparison.Ordinal);
}
