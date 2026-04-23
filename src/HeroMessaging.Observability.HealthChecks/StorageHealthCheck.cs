using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HeroMessaging.Observability.HealthChecks;
/// <summary>
/// Represents the message storage health check type.
/// </summary>

public class MessageStorageHealthCheck(IMessageStorage storage, TimeProvider timeProvider, string name = "message_storage") : IHealthCheck
{
    private readonly IMessageStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    /// <summary>
    /// Represents name.
    /// </summary>
    private readonly string _name = name;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    /// <summary>
    /// Executes check health async.
    /// </summary>

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var testMessage = new TestMessage(_timeProvider);

            var messageId = await _storage.StoreAsync(testMessage, (MessageStorageOptions?)null, cancellationToken).ConfigureAwait(false);
            var retrieved = await _storage.RetrieveAsync<TestMessage>(messageId, cancellationToken).ConfigureAwait(false);

            if (retrieved == null)
            {
                return HealthCheckResult.Unhealthy($"{_name}: Failed to retrieve test message");
            }

            await _storage.DeleteAsync(messageId, cancellationToken).ConfigureAwait(false);

            return HealthCheckResult.Healthy($"{_name}: Storage is operational");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Storage check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["storage_type"] = _storage.GetType().Name,
                    ["error"] = ex.Message
                });
        }
    }
    /// <summary>
    /// Represents the test message type.
    /// </summary>

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; }
        public string? CorrelationId { get; }
        public string? CausationId { get; }
        public Dictionary<string, object>? Metadata { get; }
        /// <summary>
        /// Initializes a new instance of the <see cref="TestMessage"/> class.
        /// </summary>

        public TestMessage(TimeProvider timeProvider)
        {
            Timestamp = timeProvider.GetUtcNow();
        }
    }
}
/// <summary>
/// Represents the outbox storage health check type.
/// </summary>

public class OutboxStorageHealthCheck(IOutboxStorage storage, string name = "outbox_storage") : IHealthCheck
{
    private readonly IOutboxStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly string _name = name;
    /// <summary>
    /// Executes check health async.
    /// </summary>

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Just check if we can query the storage
            var query = new OutboxQuery
            {
                Status = OutboxStatus.Pending,
                Limit = 1
            };

            await _storage.GetPendingAsync(query, cancellationToken).ConfigureAwait(false);

            return HealthCheckResult.Healthy($"{_name}: Storage is operational");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Storage check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["storage_type"] = _storage.GetType().Name,
                    ["error"] = ex.Message
                });
        }
    }
}
/// <summary>
/// Represents the inbox storage health check type.
/// </summary>

public class InboxStorageHealthCheck(IInboxStorage storage, string name = "inbox_storage") : IHealthCheck
{
    private readonly IInboxStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly string _name = name;
    /// <summary>
    /// Executes check health async.
    /// </summary>

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Just check if we can query the storage
            var query = new InboxQuery
            {
                Status = InboxStatus.Pending,
                Limit = 1
            };

            await _storage.GetPendingAsync(query, cancellationToken).ConfigureAwait(false);

            return HealthCheckResult.Healthy($"{_name}: Storage is operational");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Storage check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["storage_type"] = _storage.GetType().Name,
                    ["error"] = ex.Message
                });
        }
    }
}
/// <summary>
/// Represents the queue storage health check type.
/// </summary>

public class QueueStorageHealthCheck(IQueueStorage storage, string name = "queue_storage", string queueName = "health_check_queue") : IHealthCheck
{
    private readonly IQueueStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    /// <summary>
    /// Represents name.
    /// </summary>
    private readonly string _name = name;
    private readonly string _queueName = queueName;

    /// <summary>
    /// Checks the health of the configured queue storage.
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Just check if we can get queue depth
            var depth = await _storage.GetQueueDepthAsync(_queueName, cancellationToken).ConfigureAwait(false);

            var data = new Dictionary<string, object>
            {
                ["queue"] = _queueName,
                ["depth"] = depth
            };

            return HealthCheckResult.Healthy($"{_name}: Storage is operational", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                $"{_name}: Storage check failed",
                ex,
                new Dictionary<string, object>
                {
                    ["storage_type"] = _storage.GetType().Name,
                    ["queue"] = _queueName,
                    ["error"] = ex.Message
                });
        }
    }
}
