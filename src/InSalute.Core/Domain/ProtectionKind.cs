namespace InSalute.Core.Domain;

public enum ProtectionKind
{
    /// <summary>Referti uniti in un unico PDF cifrato AES.</summary>
    PdfAes = 0,

    /// <summary>Referti in un archivio ZIP cifrato AES-256.</summary>
    ZipAes = 1,

    /// <summary>Solo per i record importati dal vecchio sistema.</summary>
    Unknown = 2,
}
