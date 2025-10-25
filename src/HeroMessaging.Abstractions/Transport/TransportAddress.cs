namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Represents a transport-agnostic address for queues, topics, exchanges, etc.
/// Optimized as readonly record struct for zero allocations
/// </summary>
public readonly record struct TransportAddress
{
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

    public TransportAddress(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        Scheme = uri.Scheme;
        Host = uri.Host;
        Port = uri.Port > 0 ? uri.Port : null;
        VirtualHost = null;
        Path = uri.AbsolutePath.TrimStart('/');

        // Parse the path to extract name and type
        var segments = Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
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
    /// Create a queue address
    /// </summary>
    public static TransportAddress Queue(string name) => new(name, TransportAddressType.Queue);

    /// <summary>
    /// Create a topic address
    /// </summary>
    public static TransportAddress Topic(string name) => new(name, TransportAddressType.Topic);

    /// <summary>
    /// Create an exchange address
    /// </summary>
    public static TransportAddress Exchange(string name) => new(name, TransportAddressType.Exchange);

    /// <summary>
    /// Create a subscription address
    /// </summary>
    public static TransportAddress Subscription(string topicName, string subscriptionName)
    {
        return new TransportAddress($"{topicName}/subscriptions/{subscriptionName}", TransportAddressType.Subscription)
        {
            Path = $"{topicName}/subscriptions/{subscriptionName}"
        };
    }

    /// <summary>
    /// Parse a string into a TransportAddress
    /// </summary>
    public static TransportAddress Parse(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        // Try to parse as URI
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return new TransportAddress(uri);
        }

        // Otherwise treat as simple name
        return new TransportAddress(address);
    }

    /// <summary>
    /// Convert to URI format
    /// </summary>
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
