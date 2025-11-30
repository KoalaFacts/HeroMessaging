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

public record EnqueueOptions
{
    public int Priority { get; init; } = 0;
    public TimeSpan? Delay { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

public record OutboxOptions
{
    public string? Destination { get; init; }
    public int Priority { get; init; } = 0;
    public int MaxRetries { get; init; } = 3;
    public TimeSpan? RetryDelay { get; init; }
}

public record InboxOptions
{
    public string? Source { get; init; }
    public bool RequireIdempotency { get; init; } = true;
    public TimeSpan? DeduplicationWindow { get; init; }
}

public class MessagingMetrics
{
    public long CommandsSent { get; set; }
    public long QueriesSent { get; set; }
    public long EventsPublished { get; set; }
    public long MessagesQueued { get; set; }
    public long OutboxMessages { get; set; }
    public long InboxMessages { get; set; }
    public Dictionary<string, long> QueueDepths { get; set; } = [];
    public Dictionary<string, double> AverageProcessingTime { get; set; } = [];
}

public class MessagingHealth
{
    public bool IsHealthy { get; set; }
    public Dictionary<string, ComponentHealth> Components { get; set; } = [];
}

public class ComponentHealth
{
    public bool IsHealthy { get; set; }
    public string? Status { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset LastChecked { get; set; }
}
