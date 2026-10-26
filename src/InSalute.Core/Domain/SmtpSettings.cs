namespace InSalute.Core.Domain;

/// <summary>Mappati su <c>MailKit.Security.SecureSocketOptions</c>. Mai senza cifratura.</summary>
public enum SmtpSecurity
{
    SslOnConnect = 1,
    StartTls = 2,
}

/// <summary>Riga singola (<see cref="Id"/> = 1).</summary>
public class SmtpSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string Host { get; set; } = "";

    public int Port { get; set; }

    public SmtpSecurity Security { get; set; } = SmtpSecurity.SslOnConnect;

    public string UserName { get; set; } = "";

    /// <summary>Password cifrata con <c>IDataProtector</c>; non viene mai restituita alla UI.</summary>
    public string ProtectedPassword { get; set; } = "";

    public string FromAddress { get; set; } = "";

    public string FromName { get; set; } = "";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string? UpdatedByUserId { get; set; }
}
