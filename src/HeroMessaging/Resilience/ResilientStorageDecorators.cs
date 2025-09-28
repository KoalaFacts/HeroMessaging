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

    public async Task<string> Store(IMessage message, MessageStorageOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Store(message, options, cancellationToken), "StoreMessage", cancellationToken);
    }

    public async Task<T?> Retrieve<T>(string messageId, CancellationToken cancellationToken = default) where T : IMessage
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Retrieve<T>(messageId, cancellationToken), "RetrieveMessage", cancellationToken);
    }

    public async Task<IEnumerable<T>> Query<T>(MessageQuery query, CancellationToken cancellationToken = default) where T : IMessage
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Query<T>(query, cancellationToken), "QueryMessages", cancellationToken);
    }

    public async Task<bool> Delete(string messageId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Delete(messageId, cancellationToken), "DeleteMessage", cancellationToken);
    }

    public async Task<bool> Update(string messageId, IMessage message, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Update(messageId, message, cancellationToken), "UpdateMessage", cancellationToken);
    }

    public async Task<bool> Exists(string messageId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Exists(messageId, cancellationToken), "MessageExists", cancellationToken);
    }

    public async Task<long> Count(MessageQuery? query = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Count(query, cancellationToken), "CountMessages", cancellationToken);
    }

    public async Task Clear(CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Clear(cancellationToken), "ClearMessages", cancellationToken);
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

    public async Task<OutboxEntry> Add(IMessage message, Abstractions.OutboxOptions options, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Add(message, options, cancellationToken), "AddOutboxMessage", cancellationToken);
    }

    public async Task<IEnumerable<OutboxEntry>> GetPending(OutboxQuery query, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetPending(query, cancellationToken), "GetPendingOutboxMessages", cancellationToken);
    }

    public async Task<IEnumerable<OutboxEntry>> GetPending(int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetPending(limit, cancellationToken), "GetPendingOutboxMessages", cancellationToken);
    }

    public async Task<bool> MarkProcessed(string entryId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.MarkProcessed(entryId, cancellationToken), "MarkOutboxProcessed", cancellationToken);
    }

    public async Task<bool> MarkFailed(string entryId, string error, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.MarkFailed(entryId, error, cancellationToken), "MarkOutboxFailed", cancellationToken);
    }

    public async Task<bool> UpdateRetryCount(string entryId, int retryCount, DateTime? nextRetry = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.UpdateRetryCount(entryId, retryCount, nextRetry, cancellationToken), "UpdateOutboxRetryCount", cancellationToken);
    }

    public async Task<long> GetPendingCount(CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetPendingCount(cancellationToken), "GetOutboxPendingCount", cancellationToken);
    }

    public async Task<IEnumerable<OutboxEntry>> GetFailed(int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetFailed(limit, cancellationToken), "GetFailedOutboxMessages", cancellationToken);
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

    public async Task<InboxEntry?> Add(IMessage message, Abstractions.InboxOptions options, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Add(message, options, cancellationToken), "AddInboxMessage", cancellationToken);
    }

    public async Task<bool> IsDuplicate(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.IsDuplicate(messageId, window, cancellationToken), "CheckInboxDuplicate", cancellationToken);
    }

    public async Task<InboxEntry?> Get(string messageId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Get(messageId, cancellationToken), "GetInboxMessage", cancellationToken);
    }

    public async Task<bool> MarkProcessed(string messageId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.MarkProcessed(messageId, cancellationToken), "MarkInboxProcessed", cancellationToken);
    }

    public async Task<bool> MarkFailed(string messageId, string error, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.MarkFailed(messageId, error, cancellationToken), "MarkInboxFailed", cancellationToken);
    }

    public async Task<IEnumerable<InboxEntry>> GetPending(InboxQuery query, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetPending(query, cancellationToken), "GetPendingInboxMessages", cancellationToken);
    }

    public async Task<IEnumerable<InboxEntry>> GetUnprocessed(int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetUnprocessed(limit, cancellationToken), "GetUnprocessedInboxMessages", cancellationToken);
    }

    public async Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetUnprocessedCount(cancellationToken), "GetInboxUnprocessedCount", cancellationToken);
    }

    public async Task CleanupOldEntries(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.CleanupOldEntries(olderThan, cancellationToken), "CleanupOldInboxEntries", cancellationToken);
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

    public async Task<QueueEntry> Enqueue(string queueName, IMessage message, Abstractions.EnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Enqueue(queueName, message, options, cancellationToken), "EnqueueMessage", cancellationToken);
    }

    public async Task<QueueEntry?> Dequeue(string queueName, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Dequeue(queueName, cancellationToken), "DequeueMessage", cancellationToken);
    }

    public async Task<IEnumerable<QueueEntry>> Peek(string queueName, int count = 1, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Peek(queueName, count, cancellationToken), "PeekQueueMessages", cancellationToken);
    }

    public async Task<bool> Acknowledge(string queueName, string entryId, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Acknowledge(queueName, entryId, cancellationToken), "AcknowledgeMessage", cancellationToken);
    }

    public async Task<bool> Reject(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.Reject(queueName, entryId, requeue, cancellationToken), "RejectMessage", cancellationToken);
    }

    public async Task<long> GetQueueDepth(string queueName, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetQueueDepth(queueName, cancellationToken), "GetQueueDepth", cancellationToken);
    }

    public async Task<bool> CreateQueue(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.CreateQueue(queueName, options, cancellationToken), "CreateQueue", cancellationToken);
    }

    public async Task<bool> DeleteQueue(string queueName, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.DeleteQueue(queueName, cancellationToken), "DeleteQueue", cancellationToken);
    }

    public async Task<IEnumerable<string>> GetQueues(CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.GetQueues(cancellationToken), "GetQueues", cancellationToken);
    }

    public async Task<bool> QueueExists(string queueName, CancellationToken cancellationToken = default)
    {
        return await _resiliencePolicy.ExecuteAsync(async () =>
            await _inner.QueueExists(queueName, cancellationToken), "QueueExists", cancellationToken);
    }
}