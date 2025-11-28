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

    /// <summary>
    /// Validates that a SQL identifier (schema/table name) is safe to use in SQL statements.
    /// Prevents SQL injection by rejecting unsafe characters.
    /// </summary>
    private static void ValidateSqlIdentifier(string identifier, string paramName)
    {
        if (string.IsNullOrEmpty(identifier))
            throw new ArgumentException("SQL identifier cannot be null or empty.", paramName);

        // SQL Server identifiers must contain only letters, digits, and underscores
        foreach (var c in identifier)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                throw new ArgumentException(
                    $"Invalid SQL identifier '{identifier}'. Only letters, digits, and underscores are allowed.",
                    paramName);
            }
        }
    }

    /// <inheritdoc />
    public async Task InitializeSchemaAsync(string schemaName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(schemaName) || schemaName == "dbo")
        {
            return; // dbo schema always exists in SQL Server
        }

        // Validate schema name to prevent SQL injection
        ValidateSqlIdentifier(schemaName, nameof(schemaName));

        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Use parameterized query for the check, but schema name must be in EXEC string
            // Since we've validated the identifier, this is safe
            var sql = $"""
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = @SchemaName)
                BEGIN
                    EXEC('CREATE SCHEMA [{schemaName}]')
                END
                """;

            using var command = new SqlCommand(sql, connection);
            command.Transaction = _connectionProvider.GetTransaction();
            command.Parameters.AddWithValue("@SchemaName", schemaName);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!_connectionProvider.IsSharedConnection)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task ExecuteSchemaScriptAsync(string sql, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var command = new SqlCommand(sql, connection);
            command.Transaction = _connectionProvider.GetTransaction();
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!_connectionProvider.IsSharedConnection)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
