using Microsoft.Extensions.Diagnostics.HealthChecks;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.HealthChecks;

public class MessageStorageHealthCheck : IHealthCheck
{
    private readonly IMessageStorage _storage;
    private readonly string _name;

    public MessageStorageHealthCheck(IMessageStorage storage, string name = "message_storage")
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _name = name;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var testMessage = new TestMessage
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow
            };

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
        public string Id { get; set; } = string.Empty;
        public string MessageId { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}

public class OutboxStorageHealthCheck : IHealthCheck
{
    private readonly IOutboxStorage _storage;
    private readonly string _name;

    public OutboxStorageHealthCheck(IOutboxStorage storage, string name = "outbox_storage")
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _name = name;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pendingCount = await _storage.GetPendingCount(cancellationToken);
            
            var data = new Dictionary<string, object>
            {
                ["pending_messages"] = pendingCount,
                ["storage_type"] = _storage.GetType().Name
            };

            if (pendingCount > 1000)
            {
                return HealthCheckResult.Degraded(
                    $"{_name}: High number of pending messages",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"{_name}: Storage is operational",
                data);
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

public class InboxStorageHealthCheck : IHealthCheck
{
    private readonly IInboxStorage _storage;
    private readonly string _name;

    public InboxStorageHealthCheck(IInboxStorage storage, string name = "inbox_storage")
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _name = name;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var testMessageId = $"health_check_{Guid.NewGuid()}";
            
            var wasProcessed = await _storage.HasBeenProcessed(testMessageId, cancellationToken);
            
            if (!wasProcessed)
            {
                await _storage.MarkAsProcessed(testMessageId, cancellationToken);
                var nowProcessed = await _storage.HasBeenProcessed(testMessageId, cancellationToken);
                
                if (!nowProcessed)
                {
                    return HealthCheckResult.Unhealthy($"{_name}: Failed to mark message as processed");
                }
            }

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

public class QueueStorageHealthCheck : IHealthCheck
{
    private readonly IQueueStorage _storage;
    private readonly string _name;

    public QueueStorageHealthCheck(IQueueStorage storage, string name = "queue_storage")
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _name = name;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var testQueueName = $"health_check_{Guid.NewGuid()}";
            var testMessage = new TestMessage
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow
            };

            await _storage.Enqueue(testQueueName, testMessage, null, cancellationToken);
            
            var dequeued = await _storage.Dequeue<TestMessage>(testQueueName, cancellationToken);
            
            if (dequeued == null)
            {
                return HealthCheckResult.Unhealthy($"{_name}: Failed to dequeue test message");
            }

            await _storage.Acknowledge(testQueueName, dequeued.MessageId, cancellationToken);

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
        public string Id { get; set; } = string.Empty;
        public string MessageId { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}