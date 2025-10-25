namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Represents transport topology configuration (exchanges, queues, topics, bindings)
/// </summary>
public class TransportTopology
{
    /// <summary>
    /// Queues to create or configure
    /// </summary>
    public List<QueueDefinition> Queues { get; set; } = [];

    /// <summary>
    /// Topics to create or configure
    /// </summary>
    public List<TopicDefinition> Topics { get; set; } = [];

    /// <summary>
    /// Exchanges to create or configure (RabbitMQ)
    /// </summary>
    public List<ExchangeDefinition> Exchanges { get; set; } = [];

    /// <summary>
    /// Subscriptions to create or configure (Azure Service Bus, SNS/SQS)
    /// </summary>
    public List<SubscriptionDefinition> Subscriptions { get; set; } = [];

    /// <summary>
    /// Bindings between exchanges and queues (RabbitMQ)
    /// </summary>
    public List<BindingDefinition> Bindings { get; set; } = [];

    /// <summary>
    /// Add a queue definition
    /// </summary>
    public TransportTopology AddQueue(QueueDefinition queue)
    {
        Queues.Add(queue);
        return this;
    }

    /// <summary>
    /// Add a topic definition
    /// </summary>
    public TransportTopology AddTopic(TopicDefinition topic)
    {
        Topics.Add(topic);
        return this;
    }

    /// <summary>
    /// Add an exchange definition
    /// </summary>
    public TransportTopology AddExchange(ExchangeDefinition exchange)
    {
        Exchanges.Add(exchange);
        return this;
    }

    /// <summary>
    /// Add a subscription definition
    /// </summary>
    public TransportTopology AddSubscription(SubscriptionDefinition subscription)
    {
        Subscriptions.Add(subscription);
        return this;
    }

    /// <summary>
    /// Add a binding definition
    /// </summary>
    public TransportTopology AddBinding(BindingDefinition binding)
    {
        Bindings.Add(binding);
        return this;
    }
}

/// <summary>
/// Queue definition
/// </summary>
public class QueueDefinition
{
    /// <summary>
    /// Queue name
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether the queue is durable (survives broker restart)
    /// </summary>
    public bool Durable { get; set; } = true;

    /// <summary>
    /// Whether the queue is exclusive (only accessible by this connection)
    /// </summary>
    public bool Exclusive { get; set; }

    /// <summary>
    /// Whether to auto-delete the queue when no longer used
    /// </summary>
    public bool AutoDelete { get; set; }

    /// <summary>
    /// Maximum queue length
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Maximum queue size in bytes
    /// </summary>
    public long? MaxLengthBytes { get; set; }

    /// <summary>
    /// Message TTL for the queue
    /// </summary>
    public TimeSpan? MessageTtl { get; set; }

    /// <summary>
    /// Dead letter exchange
    /// </summary>
    public string? DeadLetterExchange { get; set; }

    /// <summary>
    /// Dead letter routing key
    /// </summary>
    public string? DeadLetterRoutingKey { get; set; }

    /// <summary>
    /// Maximum priority level
    /// </summary>
    public byte? MaxPriority { get; set; }

    /// <summary>
    /// Custom arguments
    /// </summary>
    public Dictionary<string, object>? Arguments { get; set; }
}

/// <summary>
/// Topic definition
/// </summary>
public class TopicDefinition
{
    /// <summary>
    /// Topic name
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether the topic is durable
    /// </summary>
    public bool Durable { get; set; } = true;

    /// <summary>
    /// Whether to enable partitioning
    /// </summary>
    public bool EnablePartitioning { get; set; }

    /// <summary>
    /// Whether to enable duplicate detection
    /// </summary>
    public bool EnableDuplicateDetection { get; set; }

    /// <summary>
    /// Duplicate detection window
    /// </summary>
    public TimeSpan? DuplicateDetectionWindow { get; set; }

    /// <summary>
    /// Default message TTL
    /// </summary>
    public TimeSpan? DefaultMessageTtl { get; set; }

    /// <summary>
    /// Maximum topic size in bytes
    /// </summary>
    public long? MaxSizeInBytes { get; set; }

    /// <summary>
    /// Custom properties
    /// </summary>
    public Dictionary<string, object>? Properties { get; set; }
}

/// <summary>
/// Exchange definition (RabbitMQ)
/// </summary>
public class ExchangeDefinition
{
    /// <summary>
    /// Exchange name
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Exchange type (direct, fanout, topic, headers)
    /// </summary>
    public ExchangeType Type { get; set; } = ExchangeType.Topic;

    /// <summary>
    /// Whether the exchange is durable
    /// </summary>
    public bool Durable { get; set; } = true;

    /// <summary>
    /// Whether to auto-delete the exchange
    /// </summary>
    public bool AutoDelete { get; set; }

    /// <summary>
    /// Whether the exchange is internal (not directly publishable)
    /// </summary>
    public bool Internal { get; set; }

    /// <summary>
    /// Custom arguments
    /// </summary>
    public Dictionary<string, object>? Arguments { get; set; }
}

/// <summary>
/// Exchange type
/// </summary>
public enum ExchangeType
{
    Direct,
    Fanout,
    Topic,
    Headers
}

/// <summary>
/// Subscription definition
/// </summary>
public class SubscriptionDefinition
{
    /// <summary>
    /// Subscription name
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Topic name to subscribe to
    /// </summary>
    public required string TopicName { get; set; }

    /// <summary>
    /// Whether to enable dead lettering on message expiration
    /// </summary>
    public bool EnableDeadLetteringOnMessageExpiration { get; set; } = true;

    /// <summary>
    /// Whether to enable dead lettering on filter evaluation exceptions
    /// </summary>
    public bool EnableDeadLetteringOnFilterEvaluationException { get; set; } = true;

    /// <summary>
    /// Maximum delivery count before dead lettering
    /// </summary>
    public int MaxDeliveryCount { get; set; } = 10;

    /// <summary>
    /// Lock duration for message processing
    /// </summary>
    public TimeSpan? LockDuration { get; set; }

    /// <summary>
    /// Message TTL
    /// </summary>
    public TimeSpan? DefaultMessageTtl { get; set; }

    /// <summary>
    /// Auto-delete on idle duration
    /// </summary>
    public TimeSpan? AutoDeleteOnIdle { get; set; }

    /// <summary>
    /// SQL filter expression
    /// </summary>
    public string? Filter { get; set; }

    /// <summary>
    /// Custom properties
    /// </summary>
    public Dictionary<string, object>? Properties { get; set; }
}

/// <summary>
/// Binding definition (RabbitMQ)
/// </summary>
public class BindingDefinition
{
    /// <summary>
    /// Source exchange name
    /// </summary>
    public required string SourceExchange { get; set; }

    /// <summary>
    /// Destination queue or exchange name
    /// </summary>
    public required string Destination { get; set; }

    /// <summary>
    /// Whether the destination is an exchange (true) or queue (false)
    /// </summary>
    public bool DestinationIsExchange { get; set; }

    /// <summary>
    /// Routing key pattern
    /// </summary>
    public string? RoutingKey { get; set; }

    /// <summary>
    /// Custom arguments
    /// </summary>
    public Dictionary<string, object>? Arguments { get; set; }
}
