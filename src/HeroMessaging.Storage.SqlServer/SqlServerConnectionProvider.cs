using HeroMessaging.Abstractions.Storage;
using Microsoft.Data.SqlClient;

namespace HeroMessaging.Storage.SqlServer;

/// <summary>
/// SQL Server connection provider for managing database connections and transactions
/// </summary>
public class SqlServerConnectionProvider : IDbConnectionProvider<SqlConnection, SqlTransaction>
{
    private readonly SqlConnection? _sharedConnection;
    private readonly SqlTransaction? _sharedTransaction;
    private readonly string _connectionString;

    /// <summary>
    /// Creates a connection provider with a connection string (standalone mode)
    /// </summary>
    public SqlServerConnectionProvider(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <summary>
    /// Creates a connection provider with a shared connection (transaction mode)
    /// </summary>
    public SqlServerConnectionProvider(SqlConnection connection, SqlTransaction? transaction = null)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<SqlConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_sharedConnection != null)
        {
            return _sharedConnection;
        }

        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <inheritdoc />
    public SqlTransaction? GetTransaction() => _sharedTransaction;

    /// <inheritdoc />
    public bool IsSharedConnection => _sharedConnection != null;

    /// <inheritdoc />
    public string ConnectionString => _connectionString;
}
