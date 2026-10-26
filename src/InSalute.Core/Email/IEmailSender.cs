namespace InSalute.Core.Email;

public enum SendErrorCode
{
    None,
    AuthenticationFailed,
    ConnectionFailed,
    RecipientRejected,
    MessageTooLarge,
    Timeout,
    Unknown,
}

public sealed record EmailAttachment(string FilePath, string FileName);

public sealed record EmailMessage(
    string ToAddress,
    string Subject,
    string Body,
    IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>Esito di un invio. <see cref="TechnicalDetails"/> è solo per gli espandibili "Dettagli tecnici".</summary>
public sealed record SendResult(bool Success, SendErrorCode ErrorCode = SendErrorCode.None, string? TechnicalDetails = null)
{
    public static SendResult Ok() => new(true);

    public static SendResult Fail(SendErrorCode code, string? technicalDetails = null) => new(false, code, technicalDetails);
}

public interface IEmailSender
{
    /// <summary>Non lancia eccezioni per errori SMTP: li restituisce in <see cref="SendResult"/>.</summary>
    Task<SendResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}
