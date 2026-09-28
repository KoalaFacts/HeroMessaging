using System.Data;
using Microsoft.Data.SqlClient;

namespace HeroMessaging.Storage.SqlServer;

internal sealed class SqlServerInboxClaim(SqlConnection connection, string resource) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        try
        {
            using var command = new SqlCommand("sp_releaseapplock", connection) { CommandType = CommandType.StoredProcedure };
            command.Parameters.Add("@Resource", SqlDbType.NVarChar, 255).Value = resource;
            command.Parameters.Add("@LockOwner", SqlDbType.NVarChar, 32).Value = "Session";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
