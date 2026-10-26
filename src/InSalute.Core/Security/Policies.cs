using InSalute.Core.Domain;

namespace InSalute.Core.Security;

public static class Policies
{
    public const string CanSend = nameof(CanSend);
    public const string CanManageUsers = nameof(CanManageUsers);
    public const string CanManageSettings = nameof(CanManageSettings);
    public const string IsSuperAdmin = nameof(IsSuperAdmin);

    /// <summary>Ruoli ammessi da ciascuna policy. L'host li registra come policy di autorizzazione.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> RolesByPolicy = new Dictionary<string, string[]>
    {
        [CanSend] = [Roles.Operatore, Roles.Manager, Roles.Admin, Roles.SuperAdmin],
        [CanManageUsers] = [Roles.Manager, Roles.Admin, Roles.SuperAdmin],
        [CanManageSettings] = [Roles.Admin, Roles.SuperAdmin],
        [IsSuperAdmin] = [Roles.SuperAdmin],
    };
}
