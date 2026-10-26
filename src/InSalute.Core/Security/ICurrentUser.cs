namespace InSalute.Core.Security;

public sealed record CurrentUserInfo(string Id, string? UserName, string? Role);

/// <summary>Utente corrente, ricavato solo dall'identità autenticata (mai da parametri della UI).</summary>
public interface ICurrentUser
{
    Task<CurrentUserInfo?> GetAsync(CancellationToken ct = default);
}
