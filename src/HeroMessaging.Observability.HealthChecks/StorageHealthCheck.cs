using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HeroMessaging.Observability.HealthChecks;

public class MessageStorageHealthCheck(IMessageStorage storage, TimeProvider timeProvider, string name = "message_storage") : IHealthCheck
{
    private readonly IMessageStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly string _name = name;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var testMessage = new TestMessage(_timeProvider);

            var messageId = await _storage.Store(testMessage, null, cancellationToken);
            var retrieved = await _storage.Retrieve<TestMessage>(messageId, cancellationToken);

            if (retrieved == null)
            {
                return HealthCheckResult.Unhealthy($"{_name}: Failed to retrieve test message");
            }

            await _storage.Delete(messageId, cancellationToken);

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

    private class TestMessage : IMessage
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; }
        public string? CorrelationId { get; }
        public string? CausationId { get; }
        public Dictionary<string, object>? Metadata { get; }

        public TestMessage(TimeProvider timeProvider)
        {
            Timestamp = timeProvider.GetUtcNow().DateTime;
        }
    }
}

public class OutboxStorageHealthCheck(IOutboxStorage storage, string name = "outbox_storage") : IHealthCheck
{
    private readonly IOutboxStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly string _name = name;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Just check if we can query the storage
            var query = new OutboxQuery
            {
                Status = OutboxEntryStatus.Pending,
                Limit = 1
            };

            await _storage.GetPending(query, cancellationToken);

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

public class InboxStorageHealthCheck(IInboxStorage storage, string name = "inbox_storage") : IHealthCheck
{
    private readonly IInboxStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly string _name = name;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Just check if we can query the storage
            var query = new InboxQuery
            {
                Status = InboxEntryStatus.Pending,
                Limit = 1
            };

            await _storage.GetPending(query, cancellationToken);

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

public class QueueStorageHealthCheck(IQueueStorage storage, string name = "queue_storage", string queueName = "health_check_queue") : IHealthCheck
{
    private readonly IQueueStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly string _name = name;
    private readonly string _queueName = queueName;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Just check if we can get queue depth
            var depth = await _storage.GetQueueDepth(_queueName, cancellationToken);

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