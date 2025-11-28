using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions;

/// <summary>
/// Provides message queue management capabilities.
/// </summary>
/// <remarks>
/// This interface is a subset of <see cref="IHeroMessaging"/> for consumers
/// that only need queue operations. Use this for dependency injection when
/// your component only needs queue capabilities.
/// </remarks>
public interface IMessageQueue
{
    /// <summary>
    /// Enqueues a message to a named queue for background processing.
    /// </summary>
    /// <param name="message">The message to enqueue.</param>
    /// <param name="queueName">The name of the target queue.</param>
    /// <param name="options">Optional configuration for the enqueue operation.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when queue functionality is not enabled. Call <c>WithQueues()</c> during configuration.</exception>
    Task EnqueueAsync(IMessage message, string queueName, EnqueueOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts processing messages from a named queue.
    /// </summary>
    /// <param name="queueName">The name of the queue to start.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when queue functionality is not enabled. Call <c>WithQueues()</c> during configuration.</exception>
    Task StartQueueAsync(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops processing messages from a named queue.
    /// </summary>
    /// <param name="queueName">The name of the queue to stop.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when queue functionality is not enabled. Call <c>WithQueues()</c> during configuration.</exception>
    Task StopQueueAsync(string queueName, CancellationToken cancellationToken = default);
}
