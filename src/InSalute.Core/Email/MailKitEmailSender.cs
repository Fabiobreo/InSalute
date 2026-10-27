using System.Net.Sockets;
using InSalute.Core.Data;
using InSalute.Core.Domain;
using InSalute.Core.Settings;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace InSalute.Core.Email;

public sealed class MailKitEmailSender(AppDbContext db, IDataProtectionProvider dataProtection) : IEmailSender
{
    private const int TimeoutMilliseconds = 60_000;

    public async Task<SendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        // Le impostazioni si rileggono a ogni invio, così le modifiche valgono subito.
        var settings = await db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == SmtpSettings.SingletonId, ct);
        if (settings is null)
        {
            return SendResult.Fail(SendErrorCode.Unknown, "Impostazioni SMTP non configurate.");
        }

        var streams = new List<Stream>();
        try
        {
            var mime = BuildMessage(settings, message, streams);

            var password = string.IsNullOrEmpty(settings.UserName)
                ? null
                : dataProtection.CreateProtector(SmtpSettingsService.PasswordPurpose).Unprotect(settings.ProtectedPassword);

            using var client = new SmtpClient { Timeout = TimeoutMilliseconds };
            return await DeliverAsync(client, settings, password, mime, ct);
        }
        catch (Exception ex)
        {
            return MapException(ex);
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Connessione, autenticazione (se <paramref name="password"/> non è null) e invio. Le eccezioni fino all'invio
    /// arrivano al chiamante; dopo che il server ha accettato il messaggio l'esito è <c>Ok</c> anche se il QUIT fallisce,
    /// altrimenti un'email consegnata risulterebbe non inviata (e verrebbe rimandata o annullata).
    /// </summary>
    public static async Task<SendResult> DeliverAsync(
        IMailTransport client, SmtpSettings settings, string? password, MimeMessage mime, CancellationToken ct)
    {
        await client.ConnectAsync(settings.Host, settings.Port, ToSocketOptions(settings.Security), ct);
        if (password is not null)
        {
            await client.AuthenticateAsync(settings.UserName, password, ct);
        }
        await client.SendAsync(mime, ct);

        try
        {
            await client.DisconnectAsync(true, CancellationToken.None);
        }
        catch (Exception)
        {
            // Il messaggio è già stato accettato dal server: un QUIT non riuscito non cambia l'esito.
        }
        return SendResult.Ok();
    }

    public static SecureSocketOptions ToSocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        _ => throw new ArgumentOutOfRangeException(nameof(security)),
    };

    /// <summary>Traduce le eccezioni di MailKit in un codice d'errore (7.4). Non lascia passare eccezioni SMTP.</summary>
    public static SendResult MapException(Exception ex)
    {
        // Solo il tipo e il messaggio dell'eccezione: mai password né indirizzi.
        var details = $"{ex.GetType().Name}: {ex.Message}";
        var code = ex switch
        {
            MailKit.Security.AuthenticationException => SendErrorCode.AuthenticationFailed,
            SslHandshakeException or SocketException => SendErrorCode.ConnectionFailed,
            SmtpCommandException { ErrorCode: SmtpErrorCode.RecipientNotAccepted } => SendErrorCode.RecipientRejected,
            SmtpCommandException { StatusCode: SmtpStatusCode.ExceededStorageAllocation } => SendErrorCode.MessageTooLarge,
            OperationCanceledException or TimeoutException => SendErrorCode.Timeout,
            _ => SendErrorCode.Unknown,
        };
        return SendResult.Fail(code, details);
    }

    private MimeMessage BuildMessage(SmtpSettings settings, EmailMessage message, List<Stream> streams)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.ToAddress));
        mime.Subject = message.Subject;

        var builder = new BodyBuilder { TextBody = message.Body };
        foreach (var attachment in message.Attachments ?? [])
        {
            var stream = File.OpenRead(attachment.FilePath);
            streams.Add(stream);
            builder.Attachments.Add(attachment.FileName, stream);
        }
        mime.Body = builder.ToMessageBody();
        return mime;
    }
}
