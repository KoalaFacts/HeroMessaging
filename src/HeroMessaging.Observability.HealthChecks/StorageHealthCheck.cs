using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HeroMessaging.Observability.HealthChecks;

/// <summary>
/// Health check implementation for IMessageStorage that verifies storage operations by performing a write-read-delete cycle.
/// </summary>
/// <remarks>
/// This health check validates that the message storage is operational by:
/// 1. Creating a test message with the current timestamp
/// 2. Storing the test message
/// 3. Retrieving the test message by its ID
/// 4. Deleting the test message
///
/// If any step fails, the health check reports as Unhealthy with error details.
/// This is an active health check that performs actual storage operations.
///
/// Example usage:
/// <code>
/// services.AddHealthChecks()
///     .AddCheck("message-storage",
///         new MessageStorageHealthCheck(messageStorage, TimeProvider.System));
/// </code>
/// </remarks>
public class MessageStorageHealthCheck(IMessageStorage storage, TimeProvider timeProvider, string name = "message_storage") : IHealthCheck
{
    private readonly IMessageStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly string _name = name;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>
    /// Checks the health of message storage by performing a write-read-delete operation cycle.
    /// </summary>
    /// <param name="context">The health check context provided by the health check system</param>
    /// <param name="cancellationToken">Cancellation token to abort the health check operation</param>
    /// <returns>A task containing the health check result indicating whether storage is operational</returns>
    /// <remarks>
    /// This method creates a test message, stores it, retrieves it, and then deletes it.
    /// If retrieval fails (returns null), the check reports Unhealthy.
    /// Any exceptions during the process result in an Unhealthy status with error details.
    /// </remarks>
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

    /// <summary>
    /// Internal test message class used for health check validation.
    /// </summary>
    private class TestMessage : IMessage
    {
        /// <summary>
        /// Gets the unique identifier for this test message.
        /// </summary>
        public Guid MessageId { get; } = Guid.NewGuid();

        /// <summary>
        /// Gets the timestamp when this test message was created.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Gets the correlation identifier (always null for test messages).
        /// </summary>
        public string? CorrelationId { get; }

        /// <summary>
        /// Gets the causation identifier (always null for test messages).
        /// </summary>
        public string? CausationId { get; }

        /// <summary>
        /// Gets the metadata dictionary (always null for test messages).
        /// </summary>
        public Dictionary<string, object>? Metadata { get; }

        /// <summary>
        /// Initializes a new instance of the TestMessage class.
        /// </summary>
        /// <param name="timeProvider">The time provider to get the current UTC time</param>
        public TestMessage(TimeProvider timeProvider)
        {
            Timestamp = timeProvider.GetUtcNow().DateTime;
        }
    }
}

/// <summary>
/// Health check implementation for IOutboxStorage that verifies the storage can be queried for pending entries.
/// </summary>
/// <remarks>
/// This health check validates that the outbox storage is operational by attempting to query
/// for pending outbox entries with a limit of 1. This is a lightweight, read-only check that
/// doesn't modify any data.
///
/// The check reports Healthy if the query succeeds, and Unhealthy if an exception occurs.
///
/// Example usage:
/// <code>
/// services.AddHealthChecks()
///     .AddCheck("outbox-storage",
///         new OutboxStorageHealthCheck(outboxStorage));
/// </code>
/// </remarks>
public class OutboxStorageHealthCheck(IOutboxStorage storage, string name = "outbox_storage") : IHealthCheck
{
    private readonly IOutboxStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly string _name = name;

    /// <summary>
    /// Checks the health of outbox storage by querying for pending entries.
    /// </summary>
    /// <param name="context">The health check context provided by the health check system</param>
    /// <param name="cancellationToken">Cancellation token to abort the health check operation</param>
    /// <returns>A task containing the health check result indicating whether storage is operational</returns>
    /// <remarks>
    /// This is a lightweight check that only performs a read query with a limit of 1.
    /// Any exceptions during the query result in an Unhealthy status with error details.
    /// </remarks>
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

/// <summary>
/// Health check implementation for IInboxStorage that verifies the storage can be queried for pending entries.
/// </summary>
/// <remarks>
/// This health check validates that the inbox storage is operational by attempting to query
/// for pending inbox entries with a limit of 1. This is a lightweight, read-only check that
/// doesn't modify any data.
///
/// The check reports Healthy if the query succeeds, and Unhealthy if an exception occurs.
///
/// Example usage:
/// <code>
/// services.AddHealthChecks()
///     .AddCheck("inbox-storage",
///         new InboxStorageHealthCheck(inboxStorage));
/// </code>
/// </remarks>
public class InboxStorageHealthCheck(IInboxStorage storage, string name = "inbox_storage") : IHealthCheck
{
    private readonly IInboxStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly string _name = name;

    /// <summary>
    /// Checks the health of inbox storage by querying for pending entries.
    /// </summary>
    /// <param name="context">The health check context provided by the health check system</param>
    /// <param name="cancellationToken">Cancellation token to abort the health check operation</param>
    /// <returns>A task containing the health check result indicating whether storage is operational</returns>
    /// <remarks>
    /// This is a lightweight check that only performs a read query with a limit of 1.
    /// Any exceptions during the query result in an Unhealthy status with error details.
    /// </remarks>
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

/// <summary>
/// Health check implementation for IQueueStorage that verifies the storage can query queue depth.
/// </summary>
/// <remarks>
/// This health check validates that the queue storage is operational by attempting to retrieve
/// the queue depth for a specified queue. This is a lightweight, read-only check that doesn't
/// modify any data.
///
/// The check reports Healthy if the query succeeds and includes the queue depth in the result data.
/// If an exception occurs, the check reports Unhealthy with error details.
///
/// Example usage:
/// <code>
/// services.AddHealthChecks()
///     .AddCheck("queue-storage",
///         new QueueStorageHealthCheck(queueStorage, "queue-storage", "my-queue"));
/// </code>
/// </remarks>
public class QueueStorageHealthCheck(IQueueStorage storage, string name = "queue_storage", string queueName = "health_check_queue") : IHealthCheck
{
    private readonly IQueueStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly string _name = name;
    private readonly string _queueName = queueName;

    /// <summary>
    /// Checks the health of queue storage by querying the depth of a specific queue.
    /// </summary>
    /// <param name="context">The health check context provided by the health check system</param>
    /// <param name="cancellationToken">Cancellation token to abort the health check operation</param>
    /// <returns>A task containing the health check result indicating whether storage is operational, including queue depth data</returns>
    /// <remarks>
    /// This is a lightweight check that only retrieves queue depth information.
    /// The result includes the queue name and depth in the diagnostic data.
    /// Any exceptions during the query result in an Unhealthy status with error details.
    /// </remarks>
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