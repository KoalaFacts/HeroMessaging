using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Npgsql;

namespace HeroMessaging.Storage.PostgreSql;

public class PostgreSqlInboxStorage : IInboxStorage
{
    private readonly NpgsqlConnection? _sharedConnection;
    private readonly NpgsqlTransaction? _sharedTransaction;
    private readonly string _connectionString;

    public PostgreSqlInboxStorage(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public PostgreSqlInboxStorage(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString;
    }

    public Task<InboxEntry?> Add(IMessage message, Abstractions.InboxOptions options, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL inbox storage implementation pending");
    }

    public Task<bool> IsDuplicate(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL inbox storage implementation pending");
    }

    public Task<InboxEntry?> Get(string messageId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL inbox storage implementation pending");
    }

    public Task<bool> MarkProcessed(string messageId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL inbox storage implementation pending");
    }

    public Task<bool> MarkFailed(string messageId, string error, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL inbox storage implementation pending");
    }

    public Task<IEnumerable<InboxEntry>> GetPending(InboxQuery query, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL inbox storage implementation pending");
    }
    
    public Task<IEnumerable<InboxEntry>> GetUnprocessed(int limit = 100, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL inbox storage implementation pending");
    }

    public Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL inbox storage implementation pending");
    }

    public Task CleanupOldEntries(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL inbox storage implementation pending");
    }
}