namespace HeroMessaging.Abstractions;

/// <summary>
/// Main entry point for the HeroMessaging framework providing unified access to
/// command, query, and event processing capabilities.
/// </summary>
/// <remarks>
/// <para>
/// IHeroMessaging serves as the primary facade for all messaging operations including:
/// </para>
/// <list type="bullet">
/// <item><description>Command execution (fire-and-forget and request-response)</description></item>
/// <item><description>Query execution</description></item>
/// <item><description>Event publishing</description></item>
/// <item><description>Queue management</description></item>
/// <item><description>Outbox/Inbox patterns for reliable messaging</description></item>
/// </list>
/// <para>
/// Register the service using <c>services.AddHeroMessaging()</c> during application startup.
/// </para>
/// <para>
/// For more focused dependency injection, you can depend on the segregated interfaces:
/// <see cref="ICommandSender"/>, <see cref="IQuerySender"/>, <see cref="IEventPublisher"/>,
/// <see cref="IMessageQueue"/>, <see cref="IReliableMessaging"/>, or <see cref="IMessagingObservability"/>.
/// </para>
/// </remarks>
public interface IHeroMessaging : ICommandSender, IQuerySender, IEventPublisher, IMessageQueue, IReliableMessaging, IMessagingObservability
{
    // All members are inherited from the segregated interfaces.
    // This interface serves as a unified facade for consumers who need all capabilities.
}

/// <summary>
/// Options for enqueueing a message to a queue.
/// </summary>
public record EnqueueOptions
{
    /// <summary>
    /// Priority level for the message. Higher values indicate higher priority. Default: 0.
    /// </summary>
    public int Priority { get; init; } = 0;

    /// <summary>
    /// Delay before the message becomes visible for processing.
    /// </summary>
    public TimeSpan? Delay { get; init; }

    /// <summary>
    /// Additional metadata to include with the enqueued message.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Options for storing a message in the outbox for reliable delivery.
/// </summary>
public record OutboxOptions
{
    /// <summary>
    /// Target destination for the message delivery.
    /// </summary>
    public string? Destination { get; init; }

    /// <summary>
    /// Priority level for delivery. Higher values indicate higher priority. Default: 0.
    /// </summary>
    public int Priority { get; init; } = 0;

    /// <summary>
    /// Maximum number of delivery retry attempts. Default: 3.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Delay between retry attempts.
    /// </summary>
    public TimeSpan? RetryDelay { get; init; }
}

/// <summary>
/// Options for storing a message in the inbox for idempotent processing.
/// </summary>
public record InboxOptions
{
    /// <summary>
    /// Source identifier where the message originated.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Whether to require idempotency checking for duplicate detection. Default: true.
    /// </summary>
    public bool RequireIdempotency { get; init; } = true;

    /// <summary>
    /// Time window for duplicate message detection.
    /// </summary>
    public TimeSpan? DeduplicationWindow { get; init; }
}

/// <summary>
/// Metrics about messaging system performance and throughput.
/// </summary>
public class MessagingMetrics
{
    /// <summary>
    /// Total number of commands sent.
    /// </summary>
    public long CommandsSent { get; set; }

    /// <summary>
    /// Total number of queries sent.
    /// </summary>
    public long QueriesSent { get; set; }

    /// <summary>
    /// Total number of events published.
    /// </summary>
    public long EventsPublished { get; set; }

    /// <summary>
    /// Total number of messages enqueued.
    /// </summary>
    public long MessagesQueued { get; set; }

    /// <summary>
    /// Total number of messages in the outbox.
    /// </summary>
    public long OutboxMessages { get; set; }

    /// <summary>
    /// Total number of messages in the inbox.
    /// </summary>
    public long InboxMessages { get; set; }

    /// <summary>
    /// Current queue depths by queue name.
    /// </summary>
    public Dictionary<string, long> QueueDepths { get; set; } = [];

    /// <summary>
    /// Average processing time in milliseconds by message type.
    /// </summary>
    public Dictionary<string, double> AverageProcessingTime { get; set; } = [];
}

/// <summary>
/// Overall health status of the messaging system.
/// </summary>
public class MessagingHealth
{
    /// <summary>
    /// Whether the messaging system is healthy overall.
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Health status of individual components by name.
    /// </summary>
    public Dictionary<string, ComponentHealth> Components { get; set; } = [];
}

/// <summary>
/// Health status of an individual messaging component.
/// </summary>
public class ComponentHealth
{
    /// <summary>
    /// Whether this component is healthy.
    /// </summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Current status description (e.g., "Running", "Degraded").
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Additional message about the health status.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// When the health check was last performed.
    /// </summary>
    public DateTimeOffset LastChecked { get; set; }
}
