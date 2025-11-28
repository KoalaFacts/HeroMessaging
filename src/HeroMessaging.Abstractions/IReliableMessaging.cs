using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions;

/// <summary>
/// Provides reliable messaging capabilities through outbox and inbox patterns.
/// </summary>
/// <remarks>
/// This interface is a subset of <see cref="IHeroMessaging"/> for consumers
/// that only need reliable messaging patterns. Use this for dependency injection when
/// your component only needs outbox/inbox capabilities.
/// </remarks>
public interface IReliableMessaging
{
    /// <summary>
    /// Publishes a message to the outbox for reliable delivery to external systems.
    /// </summary>
    /// <param name="message">The message to publish.</param>
    /// <param name="options">Optional configuration for the outbox operation.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when outbox functionality is not enabled. Call <c>WithOutbox()</c> during configuration.</exception>
    /// <remarks>
    /// The outbox pattern ensures messages are persisted before being sent, providing
    /// at-least-once delivery guarantees with automatic retry capabilities.
    /// </remarks>
    Task PublishToOutboxAsync(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes an incoming message through the inbox for idempotent handling.
    /// </summary>
    /// <param name="message">The message to process.</param>
    /// <param name="options">Optional configuration for the inbox operation.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when inbox functionality is not enabled. Call <c>WithInbox()</c> during configuration.</exception>
    /// <remarks>
    /// The inbox pattern provides exactly-once processing semantics by detecting and
    /// rejecting duplicate messages within the configured deduplication window.
    /// </remarks>
    Task ProcessIncomingAsync(IMessage message, InboxOptions? options = null, CancellationToken cancellationToken = default);
}
