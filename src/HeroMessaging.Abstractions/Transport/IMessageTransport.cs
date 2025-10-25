using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Core abstraction for message transport implementations
/// Provides a unified interface for various message brokers and transports
/// </summary>
public interface IMessageTransport : IAsyncDisposable, ITransportObservability
{
    /// <summary>
    /// Gets the name of the transport (e.g., "RabbitMQ", "AzureServiceBus", "InMemory")
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Connect to the transport
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the transport
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a message to a specific destination (point-to-point)
    /// </summary>
    /// <param name="destination">The destination address (queue, topic, etc.)</param>
    /// <param name="envelope">The message envelope containing the message and metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SendAsync(TransportAddress destination, TransportEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publish a message to multiple subscribers (pub/sub)
    /// </summary>
    /// <param name="topic">The topic or exchange to publish to</param>
    /// <param name="envelope">The message envelope containing the message and metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PublishAsync(TransportAddress topic, TransportEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribe to messages from a source (queue, topic, subscription)
    /// </summary>
    /// <param name="source">The source address to consume from</param>
    /// <param name="handler">The message handler callback</param>
    /// <param name="options">Consumer options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A consumer handle that can be used to stop consuming</returns>
    Task<ITransportConsumer> SubscribeAsync(
        TransportAddress source,
        Func<TransportEnvelope, MessageContext, CancellationToken, Task> handler,
        ConsumerOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create or configure transport topology (exchanges, queues, topics, subscriptions)
    /// </summary>
    /// <param name="topology">The topology configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ConfigureTopologyAsync(TransportTopology topology, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get transport-specific health information
    /// </summary>
    Task<TransportHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a consumer subscription
/// </summary>
public interface ITransportConsumer : IAsyncDisposable
{
    /// <summary>
    /// Gets the consumer identifier
    /// </summary>
    string ConsumerId { get; }

    /// <summary>
    /// Gets the source address being consumed
    /// </summary>
    TransportAddress Source { get; }

    /// <summary>
    /// Gets whether the consumer is currently active
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Stop consuming messages
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get consumer metrics
    /// </summary>
    ConsumerMetrics GetMetrics();
}
