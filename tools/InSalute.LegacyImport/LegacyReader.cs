using Microsoft.Data.SqlClient;

namespace InSalute.LegacyImport;

/// <summary>Legge il vecchio database con query semplici (niente EDMX, niente BusinessLogic).</summary>
public static class LegacyReader
{
    public static async Task<LegacyData> ReadAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var users = new List<LegacyUser>();
        await using (var command = new SqlCommand("SELECT id, email, username, creation_date, role FROM Users", connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                users.Add(new LegacyUser(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3), reader.GetString(4)));
            }
        }

        var logs = new List<LegacyLog>();
        await using (var command = new SqlCommand("SELECT id, user_id, receiver_email, sending_time FROM Log", connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                logs.Add(new LegacyLog(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetDateTime(3)));
            }
        }

        return new LegacyData(users, logs);
    }
}
