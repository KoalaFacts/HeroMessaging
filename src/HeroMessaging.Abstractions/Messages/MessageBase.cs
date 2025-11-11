namespace HeroMessaging.Abstractions.Messages;

/// <summary>
/// Base record for implementing messages with correlation tracking
/// Provides a convenient way to create messages without implementing all properties manually
/// </summary>
public abstract record MessageBase : IMessage
{
    /// <summary>
    /// Unique identifier for this message
    /// </summary>
    public Guid MessageId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// When this message was created. Defaults to system time when not explicitly set.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = TimeProvider.System.GetUtcNow();

    /// <summary>
    /// Correlation identifier for linking related messages in a workflow
    /// All messages in the same workflow/saga should share the same CorrelationId
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Causation identifier indicating which message directly caused this message
    /// Forms a chain of causality: Message A (causes) → Message B (causes) → Message C
    /// Used for distributed tracing and debugging workflows
    /// </summary>
    public string? CausationId { get; init; }

    /// <summary>
    /// Additional metadata for extensibility
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Base record for implementing messages that expect a response
/// </summary>
/// <typeparam name="TResponse">Type of the expected response</typeparam>
public abstract record MessageBase<TResponse> : MessageBase, IMessage<TResponse>
{
}
