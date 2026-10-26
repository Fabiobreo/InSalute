namespace InSalute.Core.Domain;

public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Operatore = "Operatore";

    public static readonly IReadOnlyList<string> All = [SuperAdmin, Admin, Manager, Operatore];
}
