using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Data.SqlClient;

namespace HeroMessaging.Storage.SqlServer;

public class SqlServerInboxStorage : IInboxStorage
{
    private readonly SqlConnection? _sharedConnection;
    private readonly SqlTransaction? _sharedTransaction;
    private readonly string _connectionString;

    public SqlServerInboxStorage(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public SqlServerInboxStorage(SqlConnection connection, SqlTransaction? transaction)
    {
        _sharedConnection = connection ?? throw new ArgumentNullException(nameof(connection));
        _sharedTransaction = transaction;
        _connectionString = connection.ConnectionString;
    }

    public Task<InboxEntry?> Add(IMessage message, Abstractions.InboxOptions options, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Inbox storage implementation pending");
    }

    public Task<bool> IsDuplicate(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Inbox storage implementation pending");
    }

    public Task<InboxEntry?> Get(string messageId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Inbox storage implementation pending");
    }

    public Task<bool> MarkProcessed(string messageId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Inbox storage implementation pending");
    }

    public Task<bool> MarkFailed(string messageId, string error, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Inbox storage implementation pending");
    }

    public Task<IEnumerable<InboxEntry>> GetPending(InboxQuery query, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Inbox storage implementation pending");
    }
    
    public Task<IEnumerable<InboxEntry>> GetUnprocessed(int limit = 100, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Inbox storage implementation pending");
    }

    public Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Inbox storage implementation pending");
    }

    public Task CleanupOldEntries(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Inbox storage implementation pending");
    }
}

