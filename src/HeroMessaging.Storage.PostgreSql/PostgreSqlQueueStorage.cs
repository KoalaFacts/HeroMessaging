using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Npgsql;

namespace HeroMessaging.Storage.PostgreSql;

public class PostgreSqlQueueStorage : IQueueStorage
{
    private readonly NpgsqlConnection? _sharedConnection;
    private readonly NpgsqlTransaction? _sharedTransaction;
    private readonly string _connectionString;

    public PostgreSqlQueueStorage(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public PostgreSqlQueueStorage(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString;
    }

    public Task<QueueEntry> Enqueue(string queueName, IMessage message, Abstractions.EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL queue storage implementation pending");
    }

    public Task<QueueEntry?> Dequeue(string queueName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL queue storage implementation pending");
    }

    public Task<IEnumerable<QueueEntry>> Peek(string queueName, int count = 1, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL queue storage implementation pending");
    }

    public Task<bool> Acknowledge(string queueName, string entryId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL queue storage implementation pending");
    }

    public Task<bool> Reject(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL queue storage implementation pending");
    }

    public Task<long> GetQueueDepth(string queueName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL queue storage implementation pending");
    }

    public Task<bool> CreateQueue(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL queue storage implementation pending");
    }

    public Task<bool> DeleteQueue(string queueName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL queue storage implementation pending");
    }

    public Task<IEnumerable<string>> GetQueues(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL queue storage implementation pending");
    }

    public Task<bool> QueueExists(string queueName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("PostgreSQL queue storage implementation pending");
    }
}