namespace InSalute.Core.Domain;

public enum DispatchStepStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Abandoned = 3,

    /// <summary>Solo per i record importati dal vecchio sistema.</summary>
    Unknown = 4,
}
