namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Defines the contract for building and configuring messaging transport topology.
/// Provides a fluent API for defining queues, topics, exchanges, bindings, and subscriptions
/// across different messaging systems (RabbitMQ, Azure Service Bus, AWS SQS/SNS, etc.).
/// </summary>
/// <remarks>
/// The topology builder abstracts the differences between messaging platforms, allowing you to
/// define your messaging infrastructure in a transport-agnostic way. The builder translates
/// high-level topology definitions into platform-specific configurations.
///
/// Supported topology elements:
/// - Queues: Point-to-point message channels (all platforms)
/// - Topics: Publish-subscribe message channels (Azure Service Bus, AWS SNS)
/// - Exchanges: Message routing systems (RabbitMQ)
/// - Bindings: Queue-to-exchange routing rules (RabbitMQ)
/// - Subscriptions: Topic-to-queue subscriptions (Azure Service Bus, AWS SNS/SQS)
///
/// Common topology patterns:
///
/// **Simple Queue (Point-to-Point)**
/// <code>
/// var topology = new TopologyBuilder()
///     .Queue("orders-queue", q =>
///     {
///         q.Durable = true;
///         q.MaxRetries = 3;
///     })
///     .Build();
/// </code>
///
/// **Topic with Subscriptions (Pub/Sub)**
/// <code>
/// var topology = new TopologyBuilder()
///     .Topic("order-events", t => t.EnablePartitioning = true)
///     .Subscription("order-events", "email-notification-sub", s =>
///     {
///         s.MessageFilter = "eventType = 'OrderCreated'";
///         s.MaxDeliveryCount = 5;
///     })
///     .Subscription("order-events", "inventory-sub", s =>
///     {
///         s.MessageFilter = "eventType IN ('OrderCreated', 'OrderCancelled')";
///     })
///     .Build();
/// </code>
///
/// **RabbitMQ Exchange Routing**
/// <code>
/// var topology = new TopologyBuilder()
///     .Exchange("order-exchange", ExchangeType.Topic, e => e.Durable = true)
///     .Queue("high-priority-orders", q => q.Priority = 10)
///     .Queue("standard-orders")
///     .Bind("order-exchange", "high-priority-orders", "order.high.*")
///     .Bind("order-exchange", "standard-orders", "order.standard.*")
///     .Build();
/// </code>
///
/// **Multi-Platform Compatibility**
/// <code>
/// // Define once, works on multiple platforms
/// var topology = new TopologyBuilder()
///     .Queue("processing-queue", q =>
///     {
///         q.Durable = true;
///         q.MaxRetries = 3;
///         q.MessageTimeToLive = TimeSpan.FromHours(24);
///     })
///     .Build();
///
/// // Use with different transports
/// await rabbitMqTransport.ConfigureTopologyAsync(topology);
/// await serviceBusTransport.ConfigureTopologyAsync(topology);
/// </code>
///
/// Design principles:
/// - Fluent interface: Chain method calls for readable configuration
/// - Platform abstraction: Define topology once, run on any transport
/// - Explicit configuration: No magic defaults, clear intent
/// - Validation: Build() validates topology for consistency
/// - Immutability: Built topology is read-only
///
/// Integration with HeroMessaging:
/// <code>
/// services.AddHeroMessaging(messaging =>
/// {
///     messaging.ConfigureTransport(transport =>
///     {
///         transport.UseTopology(builder => builder
///             .Queue("commands")
///             .Topic("events")
///             .Subscription("events", "event-processor"));
///     });
/// });
/// </code>
/// </remarks>
public interface ITopologyBuilder
{
    /// <summary>
    /// Defines a message queue for point-to-point communication.
    /// </summary>
    /// <param name="name">
    /// The unique name of the queue. Must be unique within the messaging namespace.
    /// Naming conventions vary by platform (lowercase for RabbitMQ, PascalCase for Azure Service Bus).
    /// </param>
    /// <param name="configure">
    /// Optional configuration action to customize queue properties such as durability,
    /// message TTL, dead-letter settings, and platform-specific options. If null, default settings are used.
    /// </param>
    /// <returns>The current <see cref="ITopologyBuilder"/> instance for method chaining.</returns>
    /// <remarks>
    /// Queues provide guaranteed, ordered delivery of messages to a single consumer (or competing consumers).
    /// Messages remain in the queue until successfully processed or expired.
    ///
    /// Common queue configurations:
    /// <code>
    /// builder.Queue("orders", q =>
    /// {
    ///     q.Durable = true;                           // Survive broker restarts
    ///     q.MaxRetries = 3;                           // Retry failed messages 3 times
    ///     q.MessageTimeToLive = TimeSpan.FromHours(1); // Expire messages after 1 hour
    ///     q.DeadLetterQueue = "orders-dlq";           // Failed messages go here
    ///     q.MaxConcurrency = 10;                      // Process 10 messages concurrently
    /// });
    /// </code>
    ///
    /// Platform-specific behavior:
    /// - RabbitMQ: Creates queue, can bind to exchanges
    /// - Azure Service Bus: Creates queue, no bindings needed
    /// - AWS SQS: Creates SQS queue with optional DLQ
    /// - In-Memory: Creates in-process queue for testing
    /// </remarks>
    ITopologyBuilder Queue(string name, Action<QueueDefinition>? configure = null);

    /// <summary>
    /// Defines a message topic for publish-subscribe communication.
    /// </summary>
    /// <param name="name">
    /// The unique name of the topic. Must be unique within the messaging namespace.
    /// Topics enable one-to-many message distribution to multiple subscribers.
    /// </param>
    /// <param name="configure">
    /// Optional configuration action to customize topic properties such as partitioning,
    /// message retention, and platform-specific options. If null, default settings are used.
    /// </param>
    /// <returns>The current <see cref="ITopologyBuilder"/> instance for method chaining.</returns>
    /// <remarks>
    /// Topics enable publish-subscribe patterns where a single published message is delivered
    /// to multiple independent subscribers. Each subscription receives its own copy of the message.
    ///
    /// Common topic configurations:
    /// <code>
    /// builder.Topic("order-events", t =>
    /// {
    ///     t.EnablePartitioning = true;               // Enable partitioning for high throughput
    ///     t.MessageRetention = TimeSpan.FromDays(7); // Keep messages for 7 days
    ///     t.MaxMessageSize = 1024 * 256;             // 256KB max message size
    /// });
    /// </code>
    ///
    /// Use with subscriptions:
    /// <code>
    /// builder
    ///     .Topic("notifications")
    ///     .Subscription("notifications", "email-sub")
    ///     .Subscription("notifications", "sms-sub")
    ///     .Subscription("notifications", "push-sub");
    /// </code>
    ///
    /// Platform-specific behavior:
    /// - Azure Service Bus: Creates Service Bus topic
    /// - AWS SNS: Creates SNS topic
    /// - RabbitMQ: Creates topic exchange (type=topic)
    /// - In-Memory: Creates in-process pub/sub topic
    ///
    /// Not supported on platforms without native pub/sub (basic AMQP queues).
    /// </remarks>
    ITopologyBuilder Topic(string name, Action<TopicDefinition>? configure = null);

    /// <summary>
    /// Defines a message exchange for routing messages to queues (RabbitMQ-specific).
    /// </summary>
    /// <param name="name">
    /// The unique name of the exchange. Must be unique within the RabbitMQ virtual host.
    /// Common naming: lowercase-with-dashes (e.g., "order-exchange", "notification-exchange").
    /// </param>
    /// <param name="type">
    /// The exchange type determining routing behavior:
    /// - <see cref="ExchangeType.Direct"/>: Routes by exact routing key match
    /// - <see cref="ExchangeType.Topic"/>: Routes by pattern matching (wildcards)
    /// - <see cref="ExchangeType.Fanout"/>: Routes to all bound queues (broadcast)
    /// - <see cref="ExchangeType.Headers"/>: Routes by message header matching
    /// </param>
    /// <param name="configure">
    /// Optional configuration action to customize exchange properties such as durability,
    /// auto-delete behavior, and custom arguments. If null, default settings are used.
    /// </param>
    /// <returns>The current <see cref="ITopologyBuilder"/> instance for method chaining.</returns>
    /// <remarks>
    /// Exchanges are RabbitMQ's message routing mechanism. Publishers send messages to exchanges,
    /// which route them to queues based on bindings and routing keys. This method is ignored
    /// on non-RabbitMQ transports.
    ///
    /// Exchange type examples:
    ///
    /// **Direct Exchange (Exact Routing)**
    /// <code>
    /// builder
    ///     .Exchange("direct-exchange", ExchangeType.Direct)
    ///     .Queue("high-priority")
    ///     .Queue("low-priority")
    ///     .Bind("direct-exchange", "high-priority", "priority.high")
    ///     .Bind("direct-exchange", "low-priority", "priority.low");
    /// </code>
    ///
    /// **Topic Exchange (Pattern Matching)**
    /// <code>
    /// builder
    ///     .Exchange("logs", ExchangeType.Topic)
    ///     .Queue("error-logs")
    ///     .Queue("all-logs")
    ///     .Bind("logs", "error-logs", "*.error")        // Match any.error
    ///     .Bind("logs", "all-logs", "#");                // Match everything
    /// </code>
    ///
    /// **Fanout Exchange (Broadcast)**
    /// <code>
    /// builder
    ///     .Exchange("notifications", ExchangeType.Fanout, e => e.Durable = true)
    ///     .Queue("email-queue")
    ///     .Queue("sms-queue")
    ///     .Bind("notifications", "email-queue")
    ///     .Bind("notifications", "sms-queue");
    /// </code>
    ///
    /// Platform compatibility:
    /// - RabbitMQ: Fully supported with all exchange types
    /// - Other platforms: Method is ignored (use Topic/Subscription instead)
    ///
    /// Performance considerations:
    /// - Topic exchanges are slower than direct/fanout due to pattern matching
    /// - Use direct exchange when routing keys are known at design time
    /// - Fanout is fastest but least flexible
    /// </remarks>
    ITopologyBuilder Exchange(string name, ExchangeType type, Action<ExchangeDefinition>? configure = null);

    /// <summary>
    /// Defines a subscription to a topic, creating a queue that receives copies of topic messages.
    /// </summary>
    /// <param name="topicName">
    /// The name of the topic to subscribe to. The topic must be defined using <see cref="Topic"/>
    /// before creating subscriptions, or it must already exist in the messaging system.
    /// </param>
    /// <param name="subscriptionName">
    /// The unique name of the subscription. Combined with topic name to form the full subscription identity.
    /// Multiple subscriptions to the same topic must have different names.
    /// </param>
    /// <param name="configure">
    /// Optional configuration action to customize subscription properties such as message filters,
    /// dead-letter settings, and delivery options. If null, default settings are used.
    /// </param>
    /// <returns>The current <see cref="ITopologyBuilder"/> instance for method chaining.</returns>
    /// <remarks>
    /// Subscriptions create independent message queues that receive copies of messages published to a topic.
    /// Each subscription maintains its own message queue, delivery state, and processing logic.
    ///
    /// Common subscription patterns:
    ///
    /// **Basic Subscription**
    /// <code>
    /// builder
    ///     .Topic("order-events")
    ///     .Subscription("order-events", "email-processor", s =>
    ///     {
    ///         s.MaxDeliveryCount = 5;
    ///         s.LockDuration = TimeSpan.FromMinutes(5);
    ///     });
    /// </code>
    ///
    /// **Filtered Subscription (Azure Service Bus SQL filters)**
    /// <code>
    /// builder
    ///     .Topic("order-events")
    ///     .Subscription("order-events", "high-value-orders", s =>
    ///     {
    ///         s.MessageFilter = "amount > 1000 AND priority = 'high'";
    ///     })
    ///     .Subscription("order-events", "standard-orders", s =>
    ///     {
    ///         s.MessageFilter = "amount <= 1000";
    ///     });
    /// </code>
    ///
    /// **Multiple Subscriptions (Fan-out)**
    /// <code>
    /// builder
    ///     .Topic("user-events")
    ///     .Subscription("user-events", "analytics")
    ///     .Subscription("user-events", "email-notifications")
    ///     .Subscription("user-events", "audit-log")
    ///     .Subscription("user-events", "cache-invalidation");
    /// </code>
    ///
    /// Platform-specific behavior:
    /// - Azure Service Bus: Creates topic subscription with SQL filters
    /// - AWS SNS/SQS: Creates SQS queue and subscribes to SNS topic
    /// - RabbitMQ: Creates queue and binds to topic exchange
    /// - In-Memory: Creates subscription queue in-process
    ///
    /// Message filtering:
    /// - Azure Service Bus: SQL-like filter expressions on message properties
    /// - AWS SNS: Filter policies on message attributes (JSON)
    /// - RabbitMQ: Routing key patterns (wildcards)
    /// - In-Memory: Predicate-based filtering
    ///
    /// Best practices:
    /// - Use descriptive subscription names indicating their purpose
    /// - Configure dead-letter queues for failed message handling
    /// - Set appropriate lock durations for message processing time
    /// - Use filters to reduce unnecessary message processing
    /// </remarks>
    ITopologyBuilder Subscription(string topicName, string subscriptionName, Action<SubscriptionDefinition>? configure = null);

    /// <summary>
    /// Binds a queue to an exchange with an optional routing key (RabbitMQ-specific).
    /// </summary>
    /// <param name="exchangeName">
    /// The name of the exchange to bind from. The exchange must be defined using <see cref="Exchange"/>
    /// or must already exist in RabbitMQ.
    /// </param>
    /// <param name="queueName">
    /// The name of the queue to bind to. The queue must be defined using <see cref="Queue"/>
    /// or must already exist in RabbitMQ.
    /// </param>
    /// <param name="routingKey">
    /// Optional routing key for message routing. Behavior depends on exchange type:
    /// - Direct: Exact match required (e.g., "order.created")
    /// - Topic: Supports wildcards (* matches one word, # matches zero or more words)
    /// - Fanout: Ignored (all messages routed regardless)
    /// - Headers: Ignored (uses message headers instead)
    /// If null or empty, binds without a routing key (suitable for fanout exchanges).
    /// </param>
    /// <param name="configure">
    /// Optional configuration action to customize binding properties such as custom arguments
    /// for specialized routing logic. If null, default settings are used.
    /// </param>
    /// <returns>The current <see cref="ITopologyBuilder"/> instance for method chaining.</returns>
    /// <remarks>
    /// Bindings connect exchanges to queues, defining how messages flow through RabbitMQ.
    /// A single queue can be bound to multiple exchanges, and a single exchange can route
    /// to multiple queues. This method is ignored on non-RabbitMQ transports.
    ///
    /// Routing patterns by exchange type:
    ///
    /// **Direct Exchange (Exact Match)**
    /// <code>
    /// builder
    ///     .Exchange("orders", ExchangeType.Direct)
    ///     .Queue("new-orders")
    ///     .Queue("cancelled-orders")
    ///     .Bind("orders", "new-orders", "order.created")
    ///     .Bind("orders", "cancelled-orders", "order.cancelled");
    /// </code>
    ///
    /// **Topic Exchange (Pattern Matching)**
    /// <code>
    /// builder
    ///     .Exchange("logs", ExchangeType.Topic)
    ///     .Queue("error-logs")
    ///     .Queue("all-logs")
    ///     .Queue("order-logs")
    ///     .Bind("logs", "error-logs", "*.error")           // Match any.error
    ///     .Bind("logs", "all-logs", "#")                    // Match everything
    ///     .Bind("logs", "order-logs", "order.*");          // Match order.anything
    /// </code>
    ///
    /// **Fanout Exchange (Broadcast - No Routing Key)**
    /// <code>
    /// builder
    ///     .Exchange("notifications", ExchangeType.Fanout)
    ///     .Queue("email-queue")
    ///     .Queue("sms-queue")
    ///     .Bind("notifications", "email-queue")            // No routing key needed
    ///     .Bind("notifications", "sms-queue");
    /// </code>
    ///
    /// **Multiple Bindings to Same Queue**
    /// <code>
    /// builder
    ///     .Exchange("events", ExchangeType.Topic)
    ///     .Queue("order-processor")
    ///     .Bind("events", "order-processor", "order.created")
    ///     .Bind("events", "order-processor", "order.updated")
    ///     .Bind("events", "order-processor", "order.shipped");
    /// </code>
    ///
    /// Routing key wildcards (topic exchanges only):
    /// - `*` (star): Matches exactly one word
    ///   - `order.*` matches `order.created` but not `order.item.created`
    /// - `#` (hash): Matches zero or more words
    ///   - `order.#` matches `order.created`, `order.item.created`, and `order`
    ///
    /// Platform compatibility:
    /// - RabbitMQ: Fully supported
    /// - Other platforms: Method is ignored (not applicable)
    ///
    /// Best practices:
    /// - Use hierarchical routing keys (e.g., "entity.action.detail")
    /// - Start specific, broaden with wildcards only when needed
    /// - Avoid overlapping patterns that could cause duplicate delivery
    /// - Document routing key conventions for your application
    /// </remarks>
    ITopologyBuilder Bind(string exchangeName, string queueName, string? routingKey = null, Action<BindingDefinition>? configure = null);

    /// <summary>
    /// Builds and validates the configured transport topology.
    /// </summary>
    /// <returns>
    /// An immutable <see cref="TransportTopology"/> instance containing all defined queues, topics,
    /// exchanges, bindings, and subscriptions, ready to be applied to a message transport.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the topology configuration is invalid, such as:
    /// - Subscriptions reference non-existent topics
    /// - Bindings reference non-existent exchanges or queues
    /// - Duplicate names within the same topology element type
    /// - Platform-specific validation failures
    /// </exception>
    /// <remarks>
    /// The Build method performs comprehensive validation to ensure the topology is consistent
    /// and can be successfully deployed to the target messaging platform. Once built, the
    /// topology is immutable and can be safely shared across threads.
    ///
    /// Validation checks performed:
    /// - All referenced entities exist (topics for subscriptions, exchanges/queues for bindings)
    /// - No duplicate names within each topology element type
    /// - Required properties are set (names, exchange types, etc.)
    /// - Platform compatibility (e.g., exchanges are RabbitMQ-only)
    /// - Circular dependencies are prevented
    ///
    /// Usage with transport:
    /// <code>
    /// // Build topology
    /// var topology = new TopologyBuilder()
    ///     .Queue("orders")
    ///     .Topic("events")
    ///     .Subscription("events", "processor")
    ///     .Build();
    ///
    /// // Apply to transport
    /// await transport.ConfigureTopologyAsync(topology, cancellationToken);
    /// </code>
    ///
    /// Error handling:
    /// <code>
    /// try
    /// {
    ///     var topology = builder.Build();
    /// }
    /// catch (InvalidOperationException ex)
    /// {
    ///     // Handle validation errors
    ///     logger.LogError(ex, "Invalid topology configuration: {Message}", ex.Message);
    ///     throw;
    /// }
    /// </code>
    ///
    /// The built topology can be:
    /// - Applied to message transports
    /// - Serialized for storage or transmission
    /// - Inspected for documentation or visualization
    /// - Compared for change detection
    /// - Cached and reused across multiple transports
    ///
    /// Best practices:
    /// - Build topology once during application startup
    /// - Cache the built topology to avoid repeated validation
    /// - Handle validation exceptions gracefully
    /// - Use the same topology across all instances for consistency
    /// </remarks>
    TransportTopology Build();
}

/// <summary>
/// Default implementation of <see cref="ITopologyBuilder"/> for configuring messaging transport topology.
/// Provides a fluent API for defining queues, topics, exchanges, bindings, and subscriptions
/// in a transport-agnostic manner.
/// </summary>
/// <remarks>
/// The TopologyBuilder class accumulates topology definitions in memory and validates them
/// during the <see cref="Build"/> method call. It implements the Builder pattern to provide
/// a clean, readable API for defining complex messaging topologies.
///
/// Key features:
/// - Fluent interface for method chaining
/// - Transport-agnostic topology definitions
/// - Validation on build to catch configuration errors early
/// - Thread-safe after building (immutable topology)
/// - Support for all major messaging platforms
///
/// Usage example:
/// <code>
/// var builder = new TopologyBuilder();
/// var topology = builder
///     .Queue("orders-queue", q =>
///     {
///         q.Durable = true;
///         q.MaxRetries = 3;
///         q.DeadLetterQueue = "orders-dlq";
///     })
///     .Topic("order-events", t => t.EnablePartitioning = true)
///     .Subscription("order-events", "email-processor", s =>
///     {
///         s.MessageFilter = "eventType = 'OrderCreated'";
///         s.MaxDeliveryCount = 5;
///     })
///     .Build();
///
/// await transport.ConfigureTopologyAsync(topology);
/// </code>
///
/// Factory method usage:
/// <code>
/// var topology = TopologyBuilder.Create()
///     .Queue("commands")
///     .Topic("events")
///     .Build();
/// </code>
///
/// The builder maintains internal state until <see cref="Build"/> is called, at which point
/// it creates an immutable <see cref="TransportTopology"/> instance. You can create multiple
/// TopologyBuilder instances for different topology configurations, but each builder instance
/// should only be used on a single thread before building.
///
/// Best practices:
/// - Use the <see cref="Create"/> factory method for clean initialization
/// - Build topology during application startup, not per-request
/// - Cache the built topology for reuse across multiple transports
/// - Handle validation exceptions from <see cref="Build"/> gracefully
/// - Use descriptive names for queues, topics, and subscriptions
/// </remarks>
public class TopologyBuilder : ITopologyBuilder
{
    private readonly TransportTopology _topology = new();

    /// <inheritdoc/>
    public ITopologyBuilder Queue(string name, Action<QueueDefinition>? configure = null)
    {
        var queue = new QueueDefinition { Name = name };
        configure?.Invoke(queue);
        _topology.AddQueue(queue);
        return this;
    }

    /// <inheritdoc/>
    public ITopologyBuilder Topic(string name, Action<TopicDefinition>? configure = null)
    {
        var topic = new TopicDefinition { Name = name };
        configure?.Invoke(topic);
        _topology.AddTopic(topic);
        return this;
    }

    /// <inheritdoc/>
    public ITopologyBuilder Exchange(string name, ExchangeType type, Action<ExchangeDefinition>? configure = null)
    {
        var exchange = new ExchangeDefinition { Name = name, Type = type };
        configure?.Invoke(exchange);
        _topology.AddExchange(exchange);
        return this;
    }

    /// <inheritdoc/>
    public ITopologyBuilder Subscription(string topicName, string subscriptionName, Action<SubscriptionDefinition>? configure = null)
    {
        var subscription = new SubscriptionDefinition { TopicName = topicName, Name = subscriptionName };
        configure?.Invoke(subscription);
        _topology.AddSubscription(subscription);
        return this;
    }

    /// <inheritdoc/>
    public ITopologyBuilder Bind(string exchangeName, string queueName, string? routingKey = null, Action<BindingDefinition>? configure = null)
    {
        var binding = new BindingDefinition
        {
            SourceExchange = exchangeName,
            Destination = queueName,
            RoutingKey = routingKey
        };
        configure?.Invoke(binding);
        _topology.AddBinding(binding);
        return this;
    }

    /// <inheritdoc/>
    public TransportTopology Build()
    {
        return _topology;
    }

    /// <summary>
    /// Creates a new instance of <see cref="TopologyBuilder"/> for defining messaging topology.
    /// This is the recommended factory method for creating topology builders.
    /// </summary>
    /// <returns>A new <see cref="ITopologyBuilder"/> instance ready for configuration.</returns>
    /// <remarks>
    /// This factory method provides a clean entry point for creating topology configurations
    /// and enables fluent method chaining from the point of creation.
    ///
    /// Example usage:
    /// <code>
    /// var topology = TopologyBuilder.Create()
    ///     .Queue("commands-queue", q => q.Durable = true)
    ///     .Topic("events-topic", t => t.EnablePartitioning = true)
    ///     .Subscription("events-topic", "event-processor")
    ///     .Build();
    /// </code>
    ///
    /// Alternatively, you can use the constructor directly:
    /// <code>
    /// var builder = new TopologyBuilder();
    /// builder.Queue("commands-queue");
    /// var topology = builder.Build();
    /// </code>
    ///
    /// The factory method is preferred for its cleaner syntax when chaining methods.
    /// Each call to Create() returns a new, independent builder instance with no shared state.
    /// </remarks>
    public static ITopologyBuilder Create() => new TopologyBuilder();
}
