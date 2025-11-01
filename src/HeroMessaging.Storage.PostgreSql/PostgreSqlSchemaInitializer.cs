using HeroMessaging.Abstractions.Storage;
using Npgsql;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL schema initializer for creating schemas and executing DDL scripts
/// </summary>
public class PostgreSqlSchemaInitializer : IDbSchemaInitializer
{
    private readonly IDbConnectionProvider<NpgsqlConnection, NpgsqlTransaction> _connectionProvider;

    public PostgreSqlSchemaInitializer(IDbConnectionProvider<NpgsqlConnection, NpgsqlTransaction> connectionProvider)
    {
        _connectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));
    }

    /// <inheritdoc />
    public async Task InitializeSchemaAsync(string schemaName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(schemaName) || schemaName == "public")
        {
            return; // public schema always exists in PostgreSQL
        }

        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        try
        {
            var sql = $"CREATE SCHEMA IF NOT EXISTS {schemaName}";
            using var command = new NpgsqlCommand(sql, connection);
            command.Transaction = _connectionProvider.GetTransaction();
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (!_connectionProvider.IsSharedConnection)
            {
                await connection.DisposeAsync();
            }
        }
    }

    /// <inheritdoc />
    public async Task ExecuteSchemaScriptAsync(string sql, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        try
        {
            using var command = new NpgsqlCommand(sql, connection);
            command.Transaction = _connectionProvider.GetTransaction();
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (!_connectionProvider.IsSharedConnection)
            {
                await connection.DisposeAsync();
            }
        }
    }
}
