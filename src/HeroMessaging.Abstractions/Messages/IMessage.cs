namespace HeroMessaging.Abstractions.Messages;

/// <summary>
/// Base interface for all messages in the HeroMessaging system
/// </summary>
public interface IMessage
{
    /// <summary>
    /// Unique identifier for this message
    /// </summary>
    Guid MessageId { get; }

    /// <summary>
    /// When this message was created
    /// </summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Correlation identifier for linking related messages in a workflow
    /// All messages in the same workflow/saga should share the same CorrelationId
    /// </summary>
    string? CorrelationId { get; }

    /// <summary>
    /// Causation identifier indicating which message directly caused this message
    /// Forms a chain of causality: Message A (causes) → Message B (causes) → Message C
    /// Used for distributed tracing and debugging workflows
    /// </summary>
    string? CausationId { get; }

    /// <summary>
    /// Additional metadata for extensibility
    /// </summary>
    Dictionary<string, object>? Metadata { get; }
}

/// <summary>
/// Message that expects a response
/// </summary>
/// <typeparam name="TResponse">Type of the expected response</typeparam>
public interface IMessage<TResponse> : IMessage
{
}
