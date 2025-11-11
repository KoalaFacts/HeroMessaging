using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;

namespace HeroMessaging.Resilience;

/// <summary>
/// Resilient decorator for message storage operations
/// </summary>
public class ResilientMessageStorageDecorator(
    IMessageStorage inner,
    IConnectionResiliencePolicy resiliencePolicy) : IMessageStorage
{
    private readonly IMessageStorage _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IConnectionResiliencePolicy _resiliencePolicy = resiliencePolicy ?? throw new ArgumentNullException(nameof(resiliencePolicy));

    public async Task<string> StoreAsync(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.StoreAsync(message, options, cancellationToken), "StoreMessage", cancellationToken);
    }

    public async Task<T?> RetrieveAsync<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.RetrieveAsync<T>(messageId, cancellationToken), "RetrieveMessage", cancellationToken);
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.QueryAsync<T>(query, cancellationToken), "QueryMessages", cancellationToken);
    }

    public async Task<bool> DeleteAsync(string messageId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.DeleteAsync(messageId, cancellationToken), "DeleteMessage", cancellationToken);
    }

    public async Task<bool> UpdateAsync(string messageId, IMessage message, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.UpdateAsync(messageId, message, cancellationToken), "UpdateMessage", cancellationToken);
    }

    public async Task<bool> ExistsAsync(string messageId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.ExistsAsync(messageId, cancellationToken), "MessageExists", cancellationToken);
    }

    public async Task<long> CountAsync(MessageQuery? query = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.CountAsync(query, cancellationToken), "CountMessages", cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.ClearAsync(cancellationToken), "ClearMessages", cancellationToken);
    }

    public async Task StoreAsync(IMessage message, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.StoreAsync(message, transaction, cancellationToken), "StoreMessageAsync", cancellationToken);
    }

    public async Task<IMessage?> RetrieveAsync(Guid messageId, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.RetrieveAsync(messageId, transaction, cancellationToken), "RetrieveMessageAsync", cancellationToken);
    }

    public async Task<List<IMessage>> QueryAsync(MessageQuery query, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.QueryAsync(query, cancellationToken), "QueryMessagesAsync", cancellationToken);
    }

    public async Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.DeleteAsync(messageId, cancellationToken), "DeleteMessageAsync", cancellationToken);
    }

    public async Task<IStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.BeginTransactionAsync(cancellationToken), "BeginTransaction", cancellationToken);
    }
}

/// <summary>
/// Resilient decorator for outbox storage operations
/// </summary>
public class ResilientOutboxStorageDecorator(
    IOutboxStorage inner,
    IConnectionResiliencePolicy resiliencePolicy) : IOutboxStorage
{
    private readonly IOutboxStorage _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IConnectionResiliencePolicy _resiliencePolicy = resiliencePolicy ?? throw new ArgumentNullException(nameof(resiliencePolicy));

    public async Task<OutboxEntry> AddAsync(IMessage message, Abstractions.OutboxOptions options, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.AddAsync(message, options, cancellationToken), "AddOutboxMessage", cancellationToken);
    }

    public async Task<IEnumerable<OutboxEntry>> GetPendingAsync(OutboxQuery query, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetPendingAsync(query, cancellationToken), "GetPendingOutboxMessages", cancellationToken);
    }

    public async Task<IEnumerable<OutboxEntry>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetPendingAsync(limit, cancellationToken), "GetPendingOutboxMessages", cancellationToken);
    }

    public async Task<bool> MarkProcessedAsync(string entryId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.MarkProcessedAsync(entryId, cancellationToken), "MarkOutboxProcessed", cancellationToken);
    }

    public async Task<bool> MarkFailedAsync(string entryId, string error, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.MarkFailedAsync(entryId, error, cancellationToken), "MarkOutboxFailed", cancellationToken);
    }

    public async Task<bool> UpdateRetryCountAsync(string entryId, int retryCount, DateTimeOffset? nextRetry = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.UpdateRetryCountAsync(entryId, retryCount, nextRetry, cancellationToken), "UpdateOutboxRetryCount", cancellationToken);
    }

    public async Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetPendingCountAsync(cancellationToken), "GetOutboxPendingCount", cancellationToken);
    }

    public async Task<IEnumerable<OutboxEntry>> GetFailedAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetFailedAsync(limit, cancellationToken), "GetFailedOutboxMessages", cancellationToken);
    }
}

/// <summary>
/// Resilient decorator for inbox storage operations
/// </summary>
public class ResilientInboxStorageDecorator(
    IInboxStorage inner,
    IConnectionResiliencePolicy resiliencePolicy) : IInboxStorage
{
    private readonly IInboxStorage _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IConnectionResiliencePolicy _resiliencePolicy = resiliencePolicy ?? throw new ArgumentNullException(nameof(resiliencePolicy));

    public async Task<InboxEntry?> AddAsync(IMessage message, Abstractions.InboxOptions options, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.AddAsync(message, options, cancellationToken), "AddInboxMessage", cancellationToken);
    }

    public async Task<bool> IsDuplicateAsync(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.IsDuplicateAsync(messageId, window, cancellationToken), "CheckInboxDuplicate", cancellationToken);
    }

    public async Task<InboxEntry?> GetAsync(string messageId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetAsync(messageId, cancellationToken), "GetInboxMessage", cancellationToken);
    }

    public async Task<bool> MarkProcessedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.MarkProcessedAsync(messageId, cancellationToken), "MarkInboxProcessed", cancellationToken);
    }

    public async Task<bool> MarkFailedAsync(string messageId, string error, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.MarkFailedAsync(messageId, error, cancellationToken), "MarkInboxFailed", cancellationToken);
    }

    public async Task<IEnumerable<InboxEntry>> GetPendingAsync(InboxQuery query, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetPendingAsync(query, cancellationToken), "GetPendingInboxMessages", cancellationToken);
    }

    public async Task<IEnumerable<InboxEntry>> GetUnprocessedAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetUnprocessedAsync(limit, cancellationToken), "GetUnprocessedInboxMessages", cancellationToken);
    }

    public async Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetUnprocessedCountAsync(cancellationToken), "GetInboxUnprocessedCount", cancellationToken);
    }

    public async Task CleanupOldEntriesAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.CleanupOldEntriesAsync(olderThan, cancellationToken), "CleanupOldInboxEntries", cancellationToken);
    }
}

/// <summary>
/// Resilient decorator for queue storage operations
/// </summary>
public class ResilientQueueStorageDecorator(
    IQueueStorage inner,
    IConnectionResiliencePolicy resiliencePolicy) : IQueueStorage
{
    private readonly IQueueStorage _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IConnectionResiliencePolicy _resiliencePolicy = resiliencePolicy ?? throw new ArgumentNullException(nameof(resiliencePolicy));

    public async Task<QueueEntry> EnqueueAsync(string queueName, IMessage message, Abstractions.EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.EnqueueAsync(queueName, message, options, cancellationToken), "EnqueueMessage", cancellationToken);
    }

    public async Task<QueueEntry?> DequeueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.DequeueAsync(queueName, cancellationToken), "DequeueMessage", cancellationToken);
    }

    public async Task<IEnumerable<QueueEntry>> PeekAsync(string queueName, int count = 1, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.PeekAsync(queueName, count, cancellationToken), "PeekQueueMessages", cancellationToken);
    }

    public async Task<bool> AcknowledgeAsync(string queueName, string entryId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.AcknowledgeAsync(queueName, entryId, cancellationToken), "AcknowledgeMessage", cancellationToken);
    }

    public async Task<bool> RejectAsync(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.RejectAsync(queueName, entryId, requeue, cancellationToken), "RejectMessage", cancellationToken);
    }

    public async Task<long> GetQueueDepthAsync(string queueName, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetQueueDepthAsync(queueName, cancellationToken), "GetQueueDepth", cancellationToken);
    }

    public async Task<bool> CreateQueueAsync(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.CreateQueueAsync(queueName, options, cancellationToken), "CreateQueue", cancellationToken);
    }

    public async Task<bool> DeleteQueueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.DeleteQueueAsync(queueName, cancellationToken), "DeleteQueue", cancellationToken);
    }

    public async Task<IEnumerable<string>> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetQueuesAsync(cancellationToken), "GetQueues", cancellationToken);
    }

    public async Task<bool> QueueExistsAsync(string queueName, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.QueueExistsAsync(queueName, cancellationToken), "QueueExists", cancellationToken);
    }
}