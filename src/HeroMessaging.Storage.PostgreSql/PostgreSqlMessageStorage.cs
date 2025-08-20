using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Npgsql;

namespace HeroMessaging.Storage.PostgreSql;

public class PostgreSqlMessageStorage : IMessageStorage
{
    private readonly NpgsqlConnection? _sharedConnection;
    private readonly NpgsqlTransaction? _sharedTransaction;
    private readonly string _connectionString;

    public PostgreSqlMessageStorage(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public PostgreSqlMessageStorage(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString;
    }

    public Task<string> Store(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL message storage implementation pending");
    }

    public Task<T?> Retrieve<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage
    {
        throw new NotImplementedException("PostgreSQL message storage implementation pending");
    }

    public Task<bool> Delete(string messageId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL message storage implementation pending");
    }

    public Task<bool> Exists(string messageId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL message storage implementation pending");
    }

    public Task<IEnumerable<T>> Query<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage
    {
        throw new NotImplementedException("PostgreSQL message storage implementation pending");
    }

    public Task<bool> Update(string messageId, IMessage message, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL message storage implementation pending");
    }

    public Task<long> Count(MessageQuery? query = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL message storage implementation pending");
    }

    public Task Clear(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL message storage implementation pending");
    }
}