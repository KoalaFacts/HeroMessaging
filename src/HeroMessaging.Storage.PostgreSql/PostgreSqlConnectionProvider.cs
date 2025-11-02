using HeroMessaging.Abstractions.Storage;
using Npgsql;

namespace HeroMessaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL connection provider for managing database connections and transactions
/// </summary>
public class PostgreSqlConnectionProvider : IDbConnectionProvider<NpgsqlConnection, NpgsqlTransaction>
{
    private readonly NpgsqlConnection? _sharedConnection;
    private readonly NpgsqlTransaction? _sharedTransaction;
    private readonly string _connectionString;

    /// <summary>
    /// Creates a connection provider with a connection string (standalone mode)
    /// </summary>
    public PostgreSqlConnectionProvider(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <summary>
    /// Creates a connection provider with a shared connection (transaction mode)
    /// </summary>
    public PostgreSqlConnectionProvider(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_sharedConnection != null)
        {
            return _sharedConnection;
        }

        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <inheritdoc />
    public NpgsqlTransaction? GetTransaction() => _sharedTransaction;

    /// <inheritdoc />
    public bool IsSharedConnection => _sharedConnection != null;

    /// <inheritdoc />
    public string ConnectionString => _connectionString;
}
