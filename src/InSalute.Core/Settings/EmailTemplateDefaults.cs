using InSalute.Core.Domain;

namespace InSalute.Core.Settings;

/// <summary>
/// Testi predefiniti dei due modelli: usati dal seed del database e dal pulsante "Ripristina testo predefinito".
/// Gli oggetti dicono chi scrive e perché: "Referto" e "Credenziali" da soli finiscono nello spam o sembrano phishing.
/// </summary>
public static class EmailTemplateDefaults
{
    public const string ReportSubject = "Il suo referto – Poliambulatorio InSalute";

    public const string ReportBody =
        "Buongiorno {NOME} {COGNOME},\n" +
        "allego il referto delle analisi.\n" +
        "Riceverà una seconda email con la password per poterlo aprire.\n" +
        "Grazie\n" +
        "Poliambulatorio InSalute";

    public const string PasswordSubject = "Password per aprire il suo referto – Poliambulatorio InSalute";

    public const string PasswordBody =
        "Buongiorno {NOME} {COGNOME},\n" +
        "questa è la password con cui aprire il file ricevuto nell'email precedente: {PASSWORD}\n" +
        "Poliambulatorio InSalute";

    public static (string Subject, string Body) For(EmailTemplateKind kind) =>
        kind == EmailTemplateKind.Password ? (PasswordSubject, PasswordBody) : (ReportSubject, ReportBody);
}
