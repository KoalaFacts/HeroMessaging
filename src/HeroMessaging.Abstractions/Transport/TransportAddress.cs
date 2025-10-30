namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Represents a transport-agnostic address for messaging endpoints (queues, topics, exchanges, subscriptions).
/// Optimized as a readonly record struct for zero allocations and high performance.
/// </summary>
/// <remarks>
/// TransportAddress provides a unified way to address messaging endpoints across different transports:
/// - RabbitMQ: queues, exchanges, and routing keys
/// - Azure Service Bus: queues, topics, and subscriptions
/// - Kafka: topics
/// - Amazon SQS/SNS: queues and topics
///
/// The address can be created in several ways:
/// - Simple name: <c>TransportAddress.Queue("orders")</c>
/// - URI format: <c>new TransportAddress(new Uri("amqp://localhost/queues/orders"))</c>
/// - Parse string: <c>TransportAddress.Parse("queue:orders")</c>
///
/// The struct is optimized as a readonly record struct for:
/// - Zero heap allocations in most scenarios
/// - Value semantics (equality by value)
/// - Immutability for thread safety
/// - High-performance messaging loops
///
/// Example usage:
/// <code>
/// // Simple queue address
/// var queue = TransportAddress.Queue("orders");
///
/// // Topic address
/// var topic = TransportAddress.Topic("order-events");
///
/// // RabbitMQ exchange with routing
/// var exchange = new TransportAddress("order-events", TransportAddressType.Exchange)
/// {
///     Path = "orders.created"  // routing key
/// };
///
/// // Azure Service Bus subscription
/// var subscription = TransportAddress.Subscription("order-events", "analytics");
///
/// // URI-based address
/// var uri = new Uri("rabbitmq://localhost:5672/exchanges/orders");
/// var address = new TransportAddress(uri);
/// </code>
/// </remarks>
public readonly record struct TransportAddress
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TransportAddress"/> struct with a name and type.
    /// </summary>
    /// <param name="name">The name of the queue, topic, exchange, or subscription</param>
    /// <param name="type">The type of address (defaults to Queue)</param>
    /// <exception cref="ArgumentNullException">Thrown when name is null</exception>
    /// <remarks>
    /// This is the most common constructor for creating transport addresses.
    ///
    /// Examples:
    /// <code>
    /// // Create a queue address
    /// var queue = new TransportAddress("orders", TransportAddressType.Queue);
    ///
    /// // Create a topic address
    /// var topic = new TransportAddress("order-events", TransportAddressType.Topic);
    ///
    /// // Type defaults to Queue if not specified
    /// var defaultQueue = new TransportAddress("notifications");
    /// </code>
    /// </remarks>
    public TransportAddress(string name, TransportAddressType type = TransportAddressType.Queue)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
        Scheme = null;
        Host = null;
        Port = null;
        VirtualHost = null;
        Path = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportAddress"/> struct from a URI.
    /// </summary>
    /// <param name="uri">The URI representing the transport address</param>
    /// <exception cref="ArgumentNullException">Thrown when uri is null</exception>
    /// <remarks>
    /// Parses a URI to extract address components:
    /// - Scheme: Transport protocol (amqp, rabbitmq, asb, kafka, sqs)
    /// - Host: Broker hostname or address
    /// - Port: Broker port number
    /// - Path: Queue/topic/exchange path and name
    ///
    /// URI format examples:
    /// - RabbitMQ: amqp://localhost:5672/queues/orders
    /// - RabbitMQ exchange: rabbitmq://localhost/exchanges/order-events
    /// - Azure Service Bus: asb://myservicebus.servicebus.windows.net/queues/orders
    /// - Kafka: kafka://localhost:9092/topics/order-events
    /// - Amazon SQS: sqs://sqs.us-east-1.amazonaws.com/123456789/orders
    ///
    /// The path is parsed to extract the address type and name:
    /// - /queues/orders -> Queue named "orders"
    /// - /topics/events -> Topic named "events"
    /// - /exchanges/commands -> Exchange named "commands"
    ///
    /// Example:
    /// <code>
    /// var uri = new Uri("amqp://rabbitmq.example.com:5672/queues/orders");
    /// var address = new TransportAddress(uri);
    /// // address.Name = "orders"
    /// // address.Type = TransportAddressType.Queue
    /// // address.Host = "rabbitmq.example.com"
    /// // address.Port = 5672
    /// </code>
    /// </remarks>
    public TransportAddress(Uri uri)
    {
        if (uri == null)
            throw new ArgumentNullException(nameof(uri));

        Scheme = uri.Scheme;
        Host = uri.Host;
        Port = uri.Port > 0 ? uri.Port : null;
        VirtualHost = null;
        Path = uri.AbsolutePath.TrimStart('/');

        // Parse the path to extract name and type
        var segments = Path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0)
        {
            var lastSegment = segments[^1];
            Type = segments.Length > 1 ? ParseType(segments[^2]) : TransportAddressType.Queue;
            Name = lastSegment;
        }
        else
        {
            Type = TransportAddressType.Queue;
            Name = string.Empty;
        }
    }

    /// <summary>
    /// The name of the queue, topic, exchange, or subscription
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The type of address (queue, topic, exchange, subscription)
    /// </summary>
    public TransportAddressType Type { get; init; }

    /// <summary>
    /// The transport scheme (e.g., "rabbitmq", "amqp", "asb", "sqs")
    /// </summary>
    public string? Scheme { get; init; }

    /// <summary>
    /// The host name or address
    /// </summary>
    public string? Host { get; init; }

    /// <summary>
    /// The port number
    /// </summary>
    public int? Port { get; init; }

    /// <summary>
    /// Virtual host (RabbitMQ specific)
    /// </summary>
    public string? VirtualHost { get; init; }

    /// <summary>
    /// Additional path segments
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Creates a queue address with the specified name.
    /// </summary>
    /// <param name="name">The queue name</param>
    /// <returns>A TransportAddress representing a queue</returns>
    /// <remarks>
    /// Queue addresses represent point-to-point messaging endpoints where
    /// messages are consumed by exactly one consumer.
    ///
    /// Example:
    /// <code>
    /// var queue = TransportAddress.Queue("orders");
    /// await transport.SendAsync(queue, envelope);
    /// </code>
    /// </remarks>
    public static TransportAddress Queue(string name) => new(name, TransportAddressType.Queue);

    /// <summary>
    /// Creates a topic address with the specified name.
    /// </summary>
    /// <param name="name">The topic name</param>
    /// <returns>A TransportAddress representing a topic</returns>
    /// <remarks>
    /// Topic addresses represent publish-subscribe messaging endpoints where
    /// messages are delivered to zero or more subscribers.
    ///
    /// Example:
    /// <code>
    /// var topic = TransportAddress.Topic("order-events");
    /// await transport.PublishAsync(topic, envelope);
    /// </code>
    /// </remarks>
    public static TransportAddress Topic(string name) => new(name, TransportAddressType.Topic);

    /// <summary>
    /// Creates an exchange address with the specified name (RabbitMQ).
    /// </summary>
    /// <param name="name">The exchange name</param>
    /// <returns>A TransportAddress representing an exchange</returns>
    /// <remarks>
    /// Exchange addresses are specific to RabbitMQ and represent routing points
    /// that direct messages to queues based on routing rules.
    ///
    /// Use the Path property to specify routing keys:
    /// <code>
    /// var exchange = TransportAddress.Exchange("order-events");
    /// var withRouting = exchange with { Path = "orders.created" };
    /// await transport.PublishAsync(withRouting, envelope);
    /// </code>
    /// </remarks>
    public static TransportAddress Exchange(string name) => new(name, TransportAddressType.Exchange);

    /// <summary>
    /// Creates a subscription address for Azure Service Bus topics.
    /// </summary>
    /// <param name="topicName">The name of the topic to subscribe to</param>
    /// <param name="subscriptionName">The name of the subscription</param>
    /// <returns>A TransportAddress representing a topic subscription</returns>
    /// <remarks>
    /// Subscription addresses are specific to Azure Service Bus and represent
    /// a filtered view of a topic. Each subscription receives a copy of messages
    /// published to the topic, optionally filtered by subscription rules.
    ///
    /// Example:
    /// <code>
    /// // Create subscription address for analytics
    /// var subscription = TransportAddress.Subscription("order-events", "analytics");
    ///
    /// // Subscribe to receive messages
    /// var consumer = await transport.SubscribeAsync(subscription, async (envelope, context, ct) =>
    /// {
    ///     await ProcessAnalyticsEvent(envelope, ct);
    ///     await context.AcknowledgeAsync(ct);
    /// });
    /// </code>
    /// </remarks>
    public static TransportAddress Subscription(string topicName, string subscriptionName)
    {
        return new TransportAddress($"{topicName}/subscriptions/{subscriptionName}", TransportAddressType.Subscription)
        {
            Path = $"{topicName}/subscriptions/{subscriptionName}"
        };
    }

    /// <summary>
    /// Parses a string into a TransportAddress.
    /// </summary>
    /// <param name="address">The address string to parse</param>
    /// <returns>A TransportAddress parsed from the string</returns>
    /// <exception cref="ArgumentException">Thrown when address is null or whitespace</exception>
    /// <remarks>
    /// Supports multiple address formats:
    ///
    /// Simple names:
    /// - "orders" -> Queue named "orders"
    ///
    /// Typed format:
    /// - "queue:orders" -> Queue named "orders"
    /// - "topic:events" -> Topic named "events"
    /// - "exchange:commands" -> Exchange named "commands"
    ///
    /// URI format:
    /// - "amqp://localhost/queues/orders"
    /// - "asb://mybus.servicebus.windows.net/topics/events"
    /// - "kafka://localhost:9092/topics/logs"
    ///
    /// Example:
    /// <code>
    /// // Parse simple name
    /// var queue = TransportAddress.Parse("orders");
    ///
    /// // Parse typed format
    /// var topic = TransportAddress.Parse("topic:order-events");
    ///
    /// // Parse URI
    /// var uri = TransportAddress.Parse("amqp://rabbitmq:5672/queues/notifications");
    /// </code>
    /// </remarks>
    public static TransportAddress Parse(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address cannot be null or whitespace.", nameof(address));

        // Try to parse as URI
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return new TransportAddress(uri);
        }

        // Otherwise treat as simple name
        return new TransportAddress(address);
    }

    /// <summary>
    /// Converts this address to a URI representation.
    /// </summary>
    /// <returns>A URI representing this transport address</returns>
    /// <remarks>
    /// Generates a URI in the format:
    /// {scheme}://{host}:{port}/{type}s/{name}
    ///
    /// If Scheme or Host are not set, defaults are used:
    /// - Scheme: "hero"
    /// - Host: "localhost"
    /// - Port: 0 (omitted if not specified)
    ///
    /// Example:
    /// <code>
    /// var queue = TransportAddress.Queue("orders");
    /// var uri = queue.ToUri();
    /// // Returns: hero://localhost/queues/orders
    ///
    /// var rabbitMq = new TransportAddress("orders", TransportAddressType.Queue)
    /// {
    ///     Scheme = "amqp",
    ///     Host = "rabbitmq.example.com",
    ///     Port = 5672
    /// };
    /// var uri2 = rabbitMq.ToUri();
    /// // Returns: amqp://rabbitmq.example.com:5672/queues/orders
    /// </code>
    /// </remarks>
    public Uri ToUri()
    {
        var scheme = Scheme ?? "hero";
        var host = Host ?? "localhost";
        var port = Port ?? 0;
        var path = Path ?? $"{Type.ToString().ToLowerInvariant()}s/{Name}";

        var builder = new UriBuilder(scheme, host, port, path);
        return builder.Uri;
    }

    /// <summary>
    /// Convert to string representation
    /// </summary>
    public override string ToString()
    {
        if (!string.IsNullOrEmpty(Scheme) && !string.IsNullOrEmpty(Host))
        {
            return ToUri().ToString();
        }

        return Type switch
        {
            TransportAddressType.Queue => $"queue:{Name}",
            TransportAddressType.Topic => $"topic:{Name}",
            TransportAddressType.Exchange => $"exchange:{Name}",
            TransportAddressType.Subscription => $"subscription:{Path ?? Name}",
            _ => Name
        };
    }

    private static TransportAddressType ParseType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "queue" or "queues" => TransportAddressType.Queue,
            "topic" or "topics" => TransportAddressType.Topic,
            "exchange" or "exchanges" => TransportAddressType.Exchange,
            "subscription" or "subscriptions" => TransportAddressType.Subscription,
            _ => TransportAddressType.Queue
        };
    }
}

/// <summary>
/// Type of transport address
/// </summary>
public enum TransportAddressType
{
    /// <summary>
    /// Point-to-point queue
    /// </summary>
    Queue,

    /// <summary>
    /// Pub/sub topic
    /// </summary>
    Topic,

    /// <summary>
    /// Exchange (RabbitMQ)
    /// </summary>
    Exchange,

    /// <summary>
    /// Subscription (Azure Service Bus)
    /// </summary>
    Subscription
}
