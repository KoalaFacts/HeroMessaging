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
            await _inner.StoreAsync(message, options, cancellationToken).ConfigureAwait(false), "StoreMessage", cancellationToken).ConfigureAwait(false);
    }

    public async Task<T?> RetrieveAsync<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.RetrieveAsync<T>(messageId, cancellationToken).ConfigureAwait(false), "RetrieveMessage", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.QueryAsync<T>(query, cancellationToken).ConfigureAwait(false), "QueryMessages", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string messageId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.DeleteAsync(messageId, cancellationToken).ConfigureAwait(false), "DeleteMessage", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UpdateAsync(string messageId, IMessage message, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.UpdateAsync(messageId, message, cancellationToken).ConfigureAwait(false), "UpdateMessage", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(string messageId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.ExistsAsync(messageId, cancellationToken).ConfigureAwait(false), "MessageExists", cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> CountAsync(MessageQuery? query = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.CountAsync(query, cancellationToken).ConfigureAwait(false), "CountMessages", cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.ClearAsync(cancellationToken).ConfigureAwait(false), "ClearMessages", cancellationToken).ConfigureAwait(false);
    }

    public async Task StoreAsync(IMessage message, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.StoreAsync(message, transaction, cancellationToken).ConfigureAwait(false), "StoreMessageAsync", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IMessage?> RetrieveAsync(Guid messageId, IStorageTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.RetrieveAsync(messageId, transaction, cancellationToken).ConfigureAwait(false), "RetrieveMessageAsync", cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<IMessage>> QueryAsync(MessageQuery query, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.QueryAsync(query, cancellationToken).ConfigureAwait(false), "QueryMessagesAsync", cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.DeleteAsync(messageId, cancellationToken).ConfigureAwait(false), "DeleteMessageAsync", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IStorageTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.BeginTransactionAsync(cancellationToken).ConfigureAwait(false), "BeginTransaction", cancellationToken).ConfigureAwait(false);
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
            await _inner.AddAsync(message, options, cancellationToken).ConfigureAwait(false), "AddOutboxMessage", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<OutboxEntry>> GetPendingAsync(OutboxQuery query, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetPendingAsync(query, cancellationToken).ConfigureAwait(false), "GetPendingOutboxMessages", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<OutboxEntry>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetPendingAsync(limit, cancellationToken).ConfigureAwait(false), "GetPendingOutboxMessages", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> MarkProcessedAsync(string entryId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.MarkProcessedAsync(entryId, cancellationToken).ConfigureAwait(false), "MarkOutboxProcessed", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> MarkFailedAsync(string entryId, string error, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.MarkFailedAsync(entryId, error, cancellationToken).ConfigureAwait(false), "MarkOutboxFailed", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UpdateRetryCountAsync(string entryId, int retryCount, DateTimeOffset? nextRetry = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.UpdateRetryCountAsync(entryId, retryCount, nextRetry, cancellationToken).ConfigureAwait(false), "UpdateOutboxRetryCount", cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetPendingCountAsync(cancellationToken).ConfigureAwait(false), "GetOutboxPendingCount", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<OutboxEntry>> GetFailedAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetFailedAsync(limit, cancellationToken).ConfigureAwait(false), "GetFailedOutboxMessages", cancellationToken).ConfigureAwait(false);
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
            await _inner.AddAsync(message, options, cancellationToken).ConfigureAwait(false), "AddInboxMessage", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsDuplicateAsync(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.IsDuplicateAsync(messageId, window, cancellationToken).ConfigureAwait(false), "CheckInboxDuplicate", cancellationToken).ConfigureAwait(false);
    }

    public async Task<InboxEntry?> GetAsync(string messageId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetAsync(messageId, cancellationToken).ConfigureAwait(false), "GetInboxMessage", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> MarkProcessedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.MarkProcessedAsync(messageId, cancellationToken).ConfigureAwait(false), "MarkInboxProcessed", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> MarkFailedAsync(string messageId, string error, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.MarkFailedAsync(messageId, error, cancellationToken).ConfigureAwait(false), "MarkInboxFailed", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<InboxEntry>> GetPendingAsync(InboxQuery query, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetPendingAsync(query, cancellationToken).ConfigureAwait(false), "GetPendingInboxMessages", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<InboxEntry>> GetUnprocessedAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetUnprocessedAsync(limit, cancellationToken).ConfigureAwait(false), "GetUnprocessedInboxMessages", cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetUnprocessedCountAsync(cancellationToken).ConfigureAwait(false), "GetInboxUnprocessedCount", cancellationToken).ConfigureAwait(false);
    }

    public async Task CleanupOldEntriesAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.CleanupOldEntriesAsync(olderThan, cancellationToken).ConfigureAwait(false), "CleanupOldInboxEntries", cancellationToken).ConfigureAwait(false);
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
            await _inner.EnqueueAsync(queueName, message, options, cancellationToken).ConfigureAwait(false), "EnqueueMessage", cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueueEntry?> DequeueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.DequeueAsync(queueName, cancellationToken).ConfigureAwait(false), "DequeueMessage", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<QueueEntry>> PeekAsync(string queueName, int count = 1, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.PeekAsync(queueName, count, cancellationToken).ConfigureAwait(false), "PeekQueueMessages", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> AcknowledgeAsync(string queueName, string entryId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.AcknowledgeAsync(queueName, entryId, cancellationToken).ConfigureAwait(false), "AcknowledgeMessage", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RejectAsync(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.RejectAsync(queueName, entryId, requeue, cancellationToken).ConfigureAwait(false), "RejectMessage", cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> GetQueueDepthAsync(string queueName, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetQueueDepthAsync(queueName, cancellationToken).ConfigureAwait(false), "GetQueueDepth", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CreateQueueAsync(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.CreateQueueAsync(queueName, options, cancellationToken).ConfigureAwait(false), "CreateQueue", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteQueueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.DeleteQueueAsync(queueName, cancellationToken).ConfigureAwait(false), "DeleteQueue", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<string>> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetQueuesAsync(cancellationToken).ConfigureAwait(false), "GetQueues", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> QueueExistsAsync(string queueName, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.QueueExistsAsync(queueName, cancellationToken).ConfigureAwait(false), "QueueExists", cancellationToken).ConfigureAwait(false);
    }
}