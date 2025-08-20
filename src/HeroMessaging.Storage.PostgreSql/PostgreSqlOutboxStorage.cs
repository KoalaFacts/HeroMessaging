using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Npgsql;

namespace HeroMessaging.Storage.PostgreSql;

public class PostgreSqlOutboxStorage : IOutboxStorage
{
    private readonly NpgsqlConnection? _sharedConnection;
    private readonly NpgsqlTransaction? _sharedTransaction;
    private readonly string _connectionString;

    public PostgreSqlOutboxStorage(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public PostgreSqlOutboxStorage(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString;
    }

    public Task<OutboxEntry> Add(IMessage message, Abstractions.OutboxOptions options, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL outbox storage implementation pending");
    }

    public Task<IEnumerable<OutboxEntry>> GetPending(int limit = 100, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL outbox storage implementation pending");
    }

    public Task<bool> MarkProcessed(string entryId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL outbox storage implementation pending");
    }

    public Task<bool> MarkFailed(string entryId, string error, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL outbox storage implementation pending");
    }

    public Task<bool> UpdateRetryCount(string entryId, int retryCount, DateTime? nextRetry = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL outbox storage implementation pending");
    }

    public Task<long> GetPendingCount(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL outbox storage implementation pending");
    }

    public Task<IEnumerable<OutboxEntry>> GetFailed(int limit = 100, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL outbox storage implementation pending");
    }
}