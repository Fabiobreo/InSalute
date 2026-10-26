namespace InSalute.Core.Domain;

public enum EmailTemplateKind
{
    /// <summary>Prima email, con l'allegato protetto.</summary>
    Report = 1,

    /// <summary>Seconda email, con la password.</summary>
    Password = 2,
}
