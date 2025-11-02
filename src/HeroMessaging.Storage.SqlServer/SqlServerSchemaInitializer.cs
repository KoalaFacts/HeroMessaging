using HeroMessaging.Abstractions.Storage;
using Microsoft.Data.SqlClient;

namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// SQL Server schema initializer for creating schemas and executing DDL scripts
/// </summary>
public class SqlServerSchemaInitializer : IDbSchemaInitializer
{
    private readonly IDbConnectionProvider<SqlConnection, SqlTransaction> _connectionProvider;

    public SqlServerSchemaInitializer(IDbConnectionProvider<SqlConnection, SqlTransaction> connectionProvider)
    {
        _connectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));
    }

    /// <inheritdoc />
    public async Task InitializeSchemaAsync(string schemaName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(schemaName) || schemaName == "dbo")
        {
            return; // dbo schema always exists in SQL Server
        }

        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        try
        {
            var sql = $"""
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schemaName}')
                BEGIN
                    EXEC('CREATE SCHEMA [{schemaName}]')
                END
                """;

            using var command = new SqlCommand(sql, connection);
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
            using var command = new SqlCommand(sql, connection);
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
