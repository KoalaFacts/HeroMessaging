using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Choreography;

namespace HeroMessaging.Abstractions.Messages;

/// <summary>
/// Extension methods for applying correlation and causation tracking to messages
/// Supports choreography pattern by automatically linking related messages in workflows
/// </summary>
// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130 // Namespace does not match folder structure
public static class ExtensionsToMessageBaseForCorrelation
{
    /// <summary>
    /// Applies correlation context to a message that extends MessageBase
    /// Sets CorrelationId from current context and CausationId to the current message ID
    /// </summary>
    /// <typeparam name="TMessage">Message type that inherits from MessageBase</typeparam>
    /// <param name="message">The message to enrich with correlation information</param>
    /// <returns>New message instance with correlation properties set</returns>
    public static TMessage WithCorrelation<TMessage>(this TMessage message)
        where TMessage : MessageBase
    {
        var correlationId = CorrelationContext.CurrentCorrelationId;
        var causationId = CorrelationContext.CurrentMessageId;

        // If no correlation context exists, use the message's own ID as correlation
        if (string.IsNullOrEmpty(correlationId))
        {
            return message;
        }

        // IMPORTANT: Preserve the original MessageId when creating the new record
        // The 'with' expression would otherwise trigger the MessageId initializer and create a new GUID
        return message with
        {
            MessageId = message.MessageId,  // Preserve original MessageId
            CorrelationId = correlationId,
            CausationId = causationId
        };
    }

    /// <summary>
    /// Applies explicit correlation and causation IDs to a message
    /// </summary>
    /// <typeparam name="TMessage">Message type that inherits from MessageBase</typeparam>
    /// <param name="message">The message to enrich</param>
    /// <param name="correlationId">Correlation identifier</param>
    /// <param name="causationId">Causation identifier (optional)</param>
    /// <returns>New message instance with correlation properties set</returns>
    public static TMessage WithCorrelation<TMessage>(
        this TMessage message,
        string correlationId,
        string? causationId = null)
        where TMessage : MessageBase
    {
        // IMPORTANT: Preserve the original MessageId when creating the new record
        return message with
        {
            MessageId = message.MessageId,  // Preserve original MessageId
            CorrelationId = correlationId,
            CausationId = causationId
        };
    }

    /// <summary>
    /// Gets correlation information from a message
    /// </summary>
    public static (string? CorrelationId, string? CausationId) GetCorrelation(this IMessage message)
    {
        return (message.CorrelationId, message.CausationId);
    }

    /// <summary>
    /// Checks if a message has correlation information
    /// </summary>
    public static bool HasCorrelation(this IMessage message)
    {
        return !string.IsNullOrEmpty(message.CorrelationId);
    }

    /// <summary>
    /// Checks if a message has causation information
    /// </summary>
    public static bool HasCausation(this IMessage message)
    {
        return !string.IsNullOrEmpty(message.CausationId);
    }

    /// <summary>
    /// Extracts correlation chain information for logging/tracing
    /// </summary>
    public static string GetCorrelationChain(this IMessage message)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(message.CorrelationId))
        {
            parts.Add($"Correlation={message.CorrelationId}");
        }

        if (!string.IsNullOrEmpty(message.CausationId))
        {
            parts.Add($"Causation={message.CausationId}");
        }

        parts.Add($"Message={message.MessageId}");

        return string.Join(" → ", parts);
    }
}
