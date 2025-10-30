using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Core abstraction for message transport implementations.
/// Provides a unified interface for various message brokers (RabbitMQ, Azure Service Bus, Kafka, SQS, etc.).
/// </summary>
/// <remarks>
/// This interface abstracts the underlying message transport infrastructure, allowing HeroMessaging
/// to work with different message brokers through a consistent API. Implementations handle:
/// - Connection management and pooling
/// - Message sending and publishing
/// - Message consumption and subscription
/// - Topology management (queues, topics, exchanges)
/// - Health monitoring and observability
/// - Automatic reconnection and error recovery
///
/// Supported transport implementations:
/// - RabbitMQ (AMQP 0.9.1)
/// - Azure Service Bus (AMQP 1.0)
/// - Apache Kafka
/// - Amazon SQS/SNS
/// - In-Memory (for testing)
///
/// Configuration examples:
/// <code>
/// // RabbitMQ
/// services.AddHeroMessaging(builder =>
/// {
///     builder.AddTransport&lt;RabbitMqTransport&gt;(options =>
///     {
///         options.Host = "localhost";
///         options.Port = 5672;
///         options.UserName = "guest";
///         options.Password = "guest";
///         options.VirtualHost = "/";
///     });
/// });
///
/// // Azure Service Bus
/// services.AddHeroMessaging(builder =>
/// {
///     builder.AddTransport&lt;AzureServiceBusTransport&gt;(options =>
///     {
///         options.ConnectionString = "Endpoint=sb://...";
///         // Or use managed identity
///         options.FullyQualifiedNamespace = "myservicebus.servicebus.windows.net";
///         options.UseManagedIdentity = true;
///     });
/// });
///
/// // Kafka
/// services.AddHeroMessaging(builder =>
/// {
///     builder.AddTransport&lt;KafkaTransport&gt;(options =>
///     {
///         options.BootstrapServers = "localhost:9092";
///         options.GroupId = "my-consumer-group";
///         options.SecurityProtocol = KafkaSecurityProtocol.SaslSsl;
///     });
/// });
/// </code>
///
/// Usage example:
/// <code>
/// var transport = serviceProvider.GetRequiredService&lt;IMessageTransport&gt;();
///
/// // Connect
/// await transport.ConnectAsync();
///
/// // Send point-to-point message
/// var envelope = new TransportEnvelope("OrderCreated", messageBytes);
/// await transport.SendAsync(TransportAddress.Queue("orders"), envelope);
///
/// // Publish to topic
/// await transport.PublishAsync(TransportAddress.Topic("order-events"), envelope);
///
/// // Subscribe to messages
/// var consumer = await transport.SubscribeAsync(
///     TransportAddress.Queue("orders"),
///     async (envelope, context, ct) =>
///     {
///         // Process message
///         await ProcessOrderAsync(envelope, ct);
///         await context.AcknowledgeAsync(ct);
///     },
///     new ConsumerOptions { PrefetchCount = 10 });
///
/// // Cleanup
/// await consumer.StopAsync();
/// await transport.DisconnectAsync();
/// </code>
/// </remarks>
public interface IMessageTransport : IAsyncDisposable, ITransportObservability
{
    /// <summary>
    /// Gets the name of the transport implementation.
    /// </summary>
    /// <value>
    /// The transport name (e.g., "RabbitMQ", "AzureServiceBus", "Kafka", "InMemory").
    /// </value>
    /// <remarks>
    /// This name is used for:
    /// - Logging and diagnostics
    /// - Metrics and health checks
    /// - Configuration resolution
    /// - Multi-transport scenarios to identify which transport handled a message
    /// </remarks>
    string Name { get; }

    /// <summary>
    /// Establishes a connection to the message transport.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the connection attempt</param>
    /// <returns>A task representing the asynchronous connection operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when already connected or connecting</exception>
    /// <exception cref="AuthenticationException">Thrown when authentication fails</exception>
    /// <exception cref="TimeoutException">Thrown when connection times out</exception>
    /// <exception cref="TransportException">Thrown when connection fails for transport-specific reasons</exception>
    /// <remarks>
    /// This method:
    /// - Establishes connection(s) to the message broker
    /// - Authenticates using configured credentials
    /// - Initializes connection pooling if enabled
    /// - Validates topology if <see cref="TransportOptions.ValidateTopologyOnStartup"/> is true
    /// - Creates topology if <see cref="TransportOptions.CreateTopologyIfNotExists"/> is true
    /// - Transitions state from Disconnected to Connecting to Connected
    ///
    /// The operation respects <see cref="TransportOptions.ConnectionTimeout"/>.
    /// If auto-reconnection is enabled, the transport will automatically reconnect on connection loss.
    ///
    /// This method is idempotent - calling it while already connected has no effect.
    ///
    /// Example:
    /// <code>
    /// try
    /// {
    ///     await transport.ConnectAsync(cancellationToken);
    ///     logger.LogInformation("Connected to {Transport}", transport.Name);
    /// }
    /// catch (AuthenticationException ex)
    /// {
    ///     logger.LogError(ex, "Authentication failed. Check credentials.");
    ///     throw;
    /// }
    /// catch (TimeoutException ex)
    /// {
    ///     logger.LogError(ex, "Connection timeout. Check network connectivity.");
    ///     throw;
    /// }
    /// </code>
    /// </remarks>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully disconnects from the message transport.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the disconnect operation</param>
    /// <returns>A task representing the asynchronous disconnect operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when already disconnected</exception>
    /// <remarks>
    /// This method:
    /// - Stops all active consumers
    /// - Completes in-flight send/publish operations
    /// - Closes all connections in the pool
    /// - Releases resources
    /// - Transitions state to Disconnecting then Disconnected
    ///
    /// The operation waits for in-flight operations to complete before closing connections.
    /// Use the cancellationToken to force immediate shutdown if needed (may lose messages).
    ///
    /// This method is idempotent - calling it while already disconnected has no effect.
    ///
    /// Always call this method before disposing the transport to ensure graceful shutdown.
    ///
    /// Example:
    /// <code>
    /// // Graceful shutdown
    /// await transport.DisconnectAsync();
    ///
    /// // Forced shutdown with timeout
    /// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    /// try
    /// {
    ///     await transport.DisconnectAsync(cts.Token);
    /// }
    /// catch (OperationCanceledException)
    /// {
    ///     logger.LogWarning("Disconnect timeout - forcing shutdown");
    /// }
    /// </code>
    /// </remarks>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to a specific destination using point-to-point messaging pattern.
    /// </summary>
    /// <param name="destination">The destination queue or endpoint to send the message to</param>
    /// <param name="envelope">The message envelope containing the serialized message and metadata</param>
    /// <param name="cancellationToken">Cancellation token to abort the send operation</param>
    /// <returns>A task representing the asynchronous send operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when destination or envelope is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when transport is not connected</exception>
    /// <exception cref="TimeoutException">Thrown when send operation times out</exception>
    /// <exception cref="TransportException">Thrown when send fails for transport-specific reasons</exception>
    /// <remarks>
    /// Point-to-point messaging ensures exactly one consumer receives the message.
    /// This is suitable for:
    /// - Command messages
    /// - Request/response patterns
    /// - Work queue distribution
    /// - Load balancing across consumers
    ///
    /// The operation respects <see cref="TransportOptions.RequestTimeout"/>.
    ///
    /// For RabbitMQ:
    /// - Uses publisher confirms if enabled
    /// - Sends to queue directly or via routing
    ///
    /// For Azure Service Bus:
    /// - Sends to queue
    /// - Supports sessions, transactions, deduplication
    ///
    /// For Kafka:
    /// - Produces to topic/partition
    /// - Supports exactly-once semantics with transactions
    ///
    /// Example:
    /// <code>
    /// var envelope = new TransportEnvelope(
    ///     messageType: "CreateOrder",
    ///     body: messageBytes,
    ///     messageId: Guid.NewGuid().ToString())
    /// {
    ///     Priority = 5,
    ///     ExpiresAt = DateTime.UtcNow.AddMinutes(5)
    /// };
    ///
    /// await transport.SendAsync(
    ///     TransportAddress.Queue("orders"),
    ///     envelope,
    ///     cancellationToken);
    /// </code>
    /// </remarks>
    Task SendAsync(TransportAddress destination, TransportEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a message to a topic using publish-subscribe messaging pattern.
    /// </summary>
    /// <param name="topic">The topic or exchange to publish the message to</param>
    /// <param name="envelope">The message envelope containing the serialized message and metadata</param>
    /// <param name="cancellationToken">Cancellation token to abort the publish operation</param>
    /// <returns>A task representing the asynchronous publish operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when topic or envelope is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when transport is not connected</exception>
    /// <exception cref="TimeoutException">Thrown when publish operation times out</exception>
    /// <exception cref="TransportException">Thrown when publish fails for transport-specific reasons</exception>
    /// <remarks>
    /// Publish-subscribe messaging delivers the message to zero or more subscribers.
    /// This is suitable for:
    /// - Event notifications
    /// - Broadcasting updates
    /// - Fan-out scenarios
    /// - Event-driven architectures
    ///
    /// The operation respects <see cref="TransportOptions.RequestTimeout"/>.
    ///
    /// For RabbitMQ:
    /// - Publishes to exchange
    /// - Supports routing keys and topic patterns
    /// - Fanout, direct, topic, and headers exchanges
    ///
    /// For Azure Service Bus:
    /// - Publishes to topic
    /// - Subscriptions receive copies via filters
    /// - Supports SQL filters and correlation filters
    ///
    /// For Kafka:
    /// - Produces to topic
    /// - All consumers in different groups receive the message
    ///
    /// Example:
    /// <code>
    /// var envelope = new TransportEnvelope(
    ///     messageType: "OrderCreated",
    ///     body: eventBytes,
    ///     correlationId: orderId);
    ///
    /// // RabbitMQ with routing key
    /// await transport.PublishAsync(
    ///     new TransportAddress("order-events", TransportAddressType.Exchange)
    ///     {
    ///         Path = "orders.created"  // routing key
    ///     },
    ///     envelope);
    ///
    /// // Azure Service Bus topic
    /// await transport.PublishAsync(
    ///     TransportAddress.Topic("order-events"),
    ///     envelope);
    /// </code>
    /// </remarks>
    Task PublishAsync(TransportAddress topic, TransportEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to messages from a queue, topic, or subscription.
    /// </summary>
    /// <param name="source">The source address to consume messages from (queue, topic, or subscription)</param>
    /// <param name="handler">The async callback invoked for each received message</param>
    /// <param name="options">Optional consumer configuration (prefetch, concurrency, auto-ack, etc.)</param>
    /// <param name="cancellationToken">Cancellation token to abort the subscription operation</param>
    /// <returns>A consumer handle that can be used to control and stop the subscription</returns>
    /// <exception cref="ArgumentNullException">Thrown when source or handler is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when transport is not connected</exception>
    /// <exception cref="TransportException">Thrown when subscription fails for transport-specific reasons</exception>
    /// <remarks>
    /// This method creates a message consumer that continuously receives and processes messages
    /// from the specified source. The handler is invoked for each message.
    ///
    /// Handler responsibilities:
    /// - Process the message
    /// - Acknowledge, reject, or defer the message via <see cref="MessageContext"/>
    /// - Handle errors appropriately
    /// - Be thread-safe if <see cref="ConsumerOptions.ConcurrentMessageLimit"/> > 1
    ///
    /// Consumer lifecycle:
    /// - Subscribe establishes the consumer
    /// - Messages are received and handler is invoked
    /// - Call <see cref="ITransportConsumer.StopAsync"/> to stop consuming
    /// - Dispose the consumer to release resources
    ///
    /// Consumer options control:
    /// - <see cref="ConsumerOptions.PrefetchCount"/>: How many messages to fetch ahead
    /// - <see cref="ConsumerOptions.ConcurrentMessageLimit"/>: Max concurrent message processing
    /// - <see cref="ConsumerOptions.AutoAcknowledge"/>: Whether to auto-ack on success
    /// - <see cref="ConsumerOptions.RequeueOnFailure"/>: Whether to requeue failed messages
    ///
    /// Example:
    /// <code>
    /// var consumer = await transport.SubscribeAsync(
    ///     TransportAddress.Queue("orders"),
    ///     async (envelope, context, ct) =>
    ///     {
    ///         try
    ///         {
    ///             // Deserialize and process
    ///             var order = Deserialize&lt;Order&gt;(envelope.Body);
    ///             await ProcessOrderAsync(order, ct);
    ///
    ///             // Acknowledge successful processing
    ///             await context.AcknowledgeAsync(ct);
    ///         }
    ///         catch (ValidationException ex)
    ///         {
    ///             // Dead letter invalid messages
    ///             await context.DeadLetterAsync(ex.Message, ct);
    ///         }
    ///         catch (Exception ex)
    ///         {
    ///             // Requeue for retry
    ///             logger.LogError(ex, "Failed to process order");
    ///             await context.RejectAsync(requeue: true, ct);
    ///         }
    ///     },
    ///     new ConsumerOptions
    ///     {
    ///         PrefetchCount = 10,
    ///         ConcurrentMessageLimit = 5,
    ///         AutoAcknowledge = false
    ///     });
    ///
    /// // Stop consuming when done
    /// await consumer.StopAsync();
    /// await consumer.DisposeAsync();
    /// </code>
    /// </remarks>
    Task<ITransportConsumer> SubscribeAsync(
        TransportAddress source,
        Func<TransportEnvelope, MessageContext, CancellationToken, Task> handler,
        ConsumerOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates the transport topology (queues, topics, exchanges, subscriptions, bindings).
    /// </summary>
    /// <param name="topology">The topology configuration defining the messaging infrastructure</param>
    /// <param name="cancellationToken">Cancellation token to abort the configuration operation</param>
    /// <returns>A task representing the asynchronous topology configuration operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when topology is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when transport is not connected</exception>
    /// <exception cref="TransportException">Thrown when topology configuration fails</exception>
    /// <remarks>
    /// This method creates or updates the messaging topology based on the transport type:
    ///
    /// RabbitMQ:
    /// - Creates exchanges, queues, and bindings
    /// - Supports direct, fanout, topic, and headers exchanges
    /// - Configures queue properties (durable, exclusive, auto-delete)
    /// - Sets up dead letter exchanges and TTL
    ///
    /// Azure Service Bus:
    /// - Creates topics, queues, and subscriptions
    /// - Configures filters on subscriptions
    /// - Sets up dead lettering and message TTL
    /// - Enables duplicate detection if needed
    ///
    /// Kafka:
    /// - Creates topics with specified partitions
    /// - Configures retention and compaction policies
    /// - Note: Exchanges and queues are not applicable
    ///
    /// The operation is typically called once during application startup to ensure
    /// the required messaging infrastructure exists.
    ///
    /// Example:
    /// <code>
    /// var topology = new TransportTopology();
    ///
    /// // RabbitMQ topology
    /// topology.AddExchange(new ExchangeDefinition
    /// {
    ///     Name = "order-events",
    ///     Type = ExchangeType.Topic,
    ///     Durable = true
    /// });
    ///
    /// topology.AddQueue(new QueueDefinition
    /// {
    ///     Name = "order-processing",
    ///     Durable = true,
    ///     MaxPriority = 10,
    ///     MessageTtl = TimeSpan.FromHours(24)
    /// });
    ///
    /// topology.AddBinding(new BindingDefinition
    /// {
    ///     SourceExchange = "order-events",
    ///     Destination = "order-processing",
    ///     RoutingKey = "order.*"
    /// });
    ///
    /// await transport.ConfigureTopologyAsync(topology);
    /// </code>
    /// </remarks>
    Task ConfigureTopologyAsync(TransportTopology topology, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current health status of the transport.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the health check</param>
    /// <returns>A task containing the transport health information</returns>
    /// <remarks>
    /// This method performs a health check and returns information about:
    /// - Overall health status (Healthy, Degraded, Unhealthy)
    /// - Current connection state
    /// - Active connections and consumers
    /// - Pending message counts
    /// - Connection uptime
    /// - Last error information
    ///
    /// The health check may perform lightweight operations to verify connectivity,
    /// such as sending a ping or checking broker availability.
    ///
    /// Use this for:
    /// - Health check endpoints (Kubernetes liveness/readiness probes)
    /// - Monitoring dashboards
    /// - Alerting systems
    /// - Load balancer health checks
    ///
    /// Example:
    /// <code>
    /// var health = await transport.GetHealthAsync();
    ///
    /// if (health.Status == HealthStatus.Healthy)
    /// {
    ///     logger.LogInformation(
    ///         "{Transport} is healthy. Uptime: {Uptime}, Active connections: {Connections}",
    ///         health.TransportName, health.Uptime, health.ActiveConnections);
    /// }
    /// else
    /// {
    ///     logger.LogWarning(
    ///         "{Transport} is {Status}. Reason: {Reason}. Last error: {Error}",
    ///         health.TransportName, health.Status, health.StatusMessage, health.LastError);
    /// }
    /// </code>
    /// </remarks>
    Task<TransportHealth> GetHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an active message consumer subscription.
/// Provides control over message consumption and access to consumer metrics.
/// </summary>
/// <remarks>
/// A consumer is created by calling <see cref="IMessageTransport.SubscribeAsync"/> and represents
/// an active subscription to a queue, topic, or subscription. The consumer continuously receives
/// messages and invokes the registered handler for each message.
///
/// Lifecycle:
/// 1. Create via <see cref="IMessageTransport.SubscribeAsync"/>
/// 2. Consumer actively processes messages
/// 3. Call <see cref="StopAsync"/> to stop consuming
/// 4. Dispose to release resources
///
/// Consumers should always be properly stopped and disposed to prevent resource leaks
/// and ensure graceful shutdown.
///
/// Example:
/// <code>
/// // Create consumer
/// var consumer = await transport.SubscribeAsync(
///     TransportAddress.Queue("orders"),
///     ProcessMessageAsync);
///
/// // Monitor metrics
/// var metrics = consumer.GetMetrics();
/// logger.LogInformation(
///     "Consumer {ConsumerId}: Processed {Count} messages, Success rate: {Rate:P}",
///     consumer.ConsumerId, metrics.MessagesProcessed, metrics.SuccessRate);
///
/// // Stop and cleanup
/// await consumer.StopAsync();
/// await consumer.DisposeAsync();
/// </code>
/// </remarks>
public interface ITransportConsumer : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique identifier for this consumer.
    /// </summary>
    /// <value>
    /// A unique string identifier for this consumer instance.
    /// </value>
    /// <remarks>
    /// This ID is used for:
    /// - Logging and diagnostics
    /// - Metrics tagging
    /// - Consumer tracking and management
    /// - Identifying consumers in monitoring tools
    ///
    /// The ID is typically auto-generated unless explicitly set via <see cref="ConsumerOptions.ConsumerId"/>.
    /// </remarks>
    string ConsumerId { get; }

    /// <summary>
    /// Gets the source address from which this consumer is receiving messages.
    /// </summary>
    /// <value>
    /// The <see cref="TransportAddress"/> representing the queue, topic, or subscription being consumed.
    /// </value>
    /// <remarks>
    /// This indicates the exact source from which messages are being received.
    /// Useful for logging, metrics, and understanding message flow.
    /// </remarks>
    TransportAddress Source { get; }

    /// <summary>
    /// Gets a value indicating whether this consumer is actively receiving and processing messages.
    /// </summary>
    /// <value>
    /// <c>true</c> if the consumer is active and processing messages; otherwise, <c>false</c>.
    /// </value>
    /// <remarks>
    /// A consumer becomes inactive when:
    /// - <see cref="StopAsync"/> is called
    /// - An unrecoverable error occurs
    /// - The underlying transport disconnects
    /// - The consumer is disposed
    ///
    /// Check this property before stopping to avoid redundant operations.
    /// </remarks>
    bool IsActive { get; }

    /// <summary>
    /// Stops the consumer from receiving new messages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort the stop operation</param>
    /// <returns>A task representing the asynchronous stop operation</returns>
    /// <remarks>
    /// This method:
    /// - Stops receiving new messages from the transport
    /// - Allows in-flight message handlers to complete
    /// - Cancels message prefetching
    /// - Transitions consumer to inactive state
    ///
    /// After stopping, the consumer can be disposed but cannot be restarted.
    /// Create a new consumer via <see cref="IMessageTransport.SubscribeAsync"/> if needed.
    ///
    /// This method is idempotent - calling it multiple times has no additional effect.
    ///
    /// The cancellationToken can be used to force immediate shutdown, but this may
    /// interrupt message handlers mid-processing.
    ///
    /// Example:
    /// <code>
    /// // Graceful shutdown
    /// if (consumer.IsActive)
    /// {
    ///     await consumer.StopAsync();
    ///     logger.LogInformation("Consumer {Id} stopped", consumer.ConsumerId);
    /// }
    ///
    /// // Forced shutdown with timeout
    /// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    /// try
    /// {
    ///     await consumer.StopAsync(cts.Token);
    /// }
    /// catch (OperationCanceledException)
    /// {
    ///     logger.LogWarning("Consumer stop timeout - forcing shutdown");
    /// }
    /// </code>
    /// </remarks>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current metrics for this consumer.
    /// </summary>
    /// <returns>
    /// A <see cref="ConsumerMetrics"/> object containing statistics about message processing.
    /// </returns>
    /// <remarks>
    /// Metrics include:
    /// - <see cref="ConsumerMetrics.MessagesReceived"/>: Total messages received
    /// - <see cref="ConsumerMetrics.MessagesProcessed"/>: Successfully processed messages
    /// - <see cref="ConsumerMetrics.MessagesFailed"/>: Failed message processing attempts
    /// - <see cref="ConsumerMetrics.MessagesAcknowledged"/>: Messages acknowledged
    /// - <see cref="ConsumerMetrics.MessagesRejected"/>: Messages rejected
    /// - <see cref="ConsumerMetrics.MessagesDeadLettered"/>: Messages moved to dead letter queue
    /// - <see cref="ConsumerMetrics.AverageProcessingDuration"/>: Average handler execution time
    /// - <see cref="ConsumerMetrics.SuccessRate"/>: Success ratio (0.0 to 1.0)
    ///
    /// Use these metrics for:
    /// - Monitoring consumer health
    /// - Performance tuning
    /// - Capacity planning
    /// - Alerting on failures
    ///
    /// Example:
    /// <code>
    /// var metrics = consumer.GetMetrics();
    ///
    /// logger.LogInformation(
    ///     "Consumer {ConsumerId} metrics: " +
    ///     "Received: {Received}, " +
    ///     "Processed: {Processed}, " +
    ///     "Failed: {Failed}, " +
    ///     "Success Rate: {SuccessRate:P}, " +
    ///     "Avg Duration: {Duration}ms",
    ///     consumer.ConsumerId,
    ///     metrics.MessagesReceived,
    ///     metrics.MessagesProcessed,
    ///     metrics.MessagesFailed,
    ///     metrics.SuccessRate,
    ///     metrics.AverageProcessingDuration.TotalMilliseconds);
    ///
    /// // Alert on high failure rate
    /// if (metrics.SuccessRate < 0.95 && metrics.TotalOperations > 100)
    /// {
    ///     await alertService.SendAlertAsync(
    ///         $"Consumer {consumer.ConsumerId} has low success rate: {metrics.SuccessRate:P}");
    /// }
    /// </code>
    /// </remarks>
    ConsumerMetrics GetMetrics();
}
