using Npgsql;

namespace HeroMessaging.Storage.PostgreSql;

internal sealed class PostgreSqlInboxClaim(NpgsqlConnection connection, string resource) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@resource, 0))", connection);
            command.Parameters.AddWithValue("resource", resource);
            await command.ExecuteScalarAsync().ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
