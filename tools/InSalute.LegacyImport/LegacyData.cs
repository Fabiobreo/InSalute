namespace InSalute.LegacyImport;

/// <summary>Riga di Users del vecchio DB. La vecchia password non viene nemmeno letta: non si migra.</summary>
public sealed record LegacyUser(long Id, string Email, string UserName, DateTime CreationDate, string Role);

/// <summary>Riga di Log del vecchio DB: una per ogni email inviata (quindi due per invio).</summary>
public sealed record LegacyLog(long Id, long UserId, string ReceiverEmail, DateTime SendingTime);

public sealed record LegacyData(IReadOnlyList<LegacyUser> Users, IReadOnlyList<LegacyLog> Logs);

/// <summary>Solo conteggi: l'import non stampa mai email o nomi.</summary>
public sealed record ImportSummary(
    int UsersRead,
    int UsersAlreadyImported,
    int UsersToCreate,
    int UsersSkipped,
    int LogRows,
    int LogRowsSkipped,
    int DispatchesResulting,
    int DispatchesAlreadyImported,
    int DispatchesToCreate);
