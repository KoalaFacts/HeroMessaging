namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Base class for transport configuration options
/// </summary>
public abstract class TransportOptions
{
    /// <summary>
    /// Transport name (e.g., "RabbitMQ", "AzureServiceBus")
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether to enable automatic reconnection
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// Reconnection retry policy
    /// </summary>
    public RetryPolicy ReconnectionPolicy { get; set; } = new()
    {
        MaxAttempts = -1, // Infinite reconnection attempts by default
        InitialDelay = TimeSpan.FromSeconds(5),
        MaxDelay = TimeSpan.FromMinutes(1),
        UseExponentialBackoff = true
    };

    /// <summary>
    /// Connection timeout
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Request timeout for send/publish operations
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to enable connection pooling
    /// </summary>
    public bool EnableConnectionPooling { get; set; } = true;

    /// <summary>
    /// Minimum number of connections in the pool
    /// </summary>
    public int MinPoolSize { get; set; } = 1;

    /// <summary>
    /// Maximum number of connections in the pool
    /// </summary>
    public int MaxPoolSize { get; set; } = 10;

    /// <summary>
    /// Connection idle timeout before being closed
    /// </summary>
    public TimeSpan ConnectionIdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether to validate topology on startup
    /// </summary>
    public bool ValidateTopologyOnStartup { get; set; } = true;

    /// <summary>
    /// Whether to create topology if it doesn't exist
    /// </summary>
    public bool CreateTopologyIfNotExists { get; set; } = true;

    /// <summary>
    /// Custom properties for transport-specific configuration
    /// </summary>
    public Dictionary<string, object>? CustomProperties { get; set; }
}

/// <summary>
/// Options for in-memory transport
/// </summary>
public class InMemoryTransportOptions : TransportOptions
{
    public InMemoryTransportOptions()
    {
        Name = "InMemory";
        EnableConnectionPooling = false; // Not needed for in-memory
    }

    /// <summary>
    /// Maximum queue length before blocking/dropping
    /// </summary>
    public int MaxQueueLength { get; set; } = 10000;

    /// <summary>
    /// Whether to drop messages when queue is full (instead of blocking)
    /// </summary>
    public bool DropWhenFull { get; set; }

    /// <summary>
    /// Whether to simulate network delays
    /// </summary>
    public bool SimulateNetworkDelay { get; set; }

    /// <summary>
    /// Simulated network delay range
    /// </summary>
    public TimeSpan SimulatedDelayMin { get; set; } = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// Simulated network delay range
    /// </summary>
    public TimeSpan SimulatedDelayMax { get; set; } = TimeSpan.FromMilliseconds(10);
}

/// <summary>
/// Options for RabbitMQ transport
/// </summary>
public class RabbitMqTransportOptions : TransportOptions
{
    public RabbitMqTransportOptions()
    {
        Name = "RabbitMQ";
    }

    /// <summary>
    /// Connection string or host name
    /// </summary>
    public required string Host { get; set; }

    /// <summary>
    /// Port number (default: 5672)
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    /// Virtual host (default: "/")
    /// </summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// User name
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Password
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Whether to use SSL/TLS
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>
    /// Heartbeat interval
    /// </summary>
    public TimeSpan Heartbeat { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Prefetch count for consumers
    /// </summary>
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>
    /// Whether to use publisher confirms
    /// </summary>
    public bool UsePublisherConfirms { get; set; } = true;

    /// <summary>
    /// Publisher confirm timeout
    /// </summary>
    public TimeSpan PublisherConfirmTimeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Options for Azure Service Bus transport
/// </summary>
public class AzureServiceBusTransportOptions : TransportOptions
{
    public AzureServiceBusTransportOptions()
    {
        Name = "AzureServiceBus";
    }

    /// <summary>
    /// Connection string
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Fully qualified namespace (alternative to connection string)
    /// </summary>
    public string? FullyQualifiedNamespace { get; set; }

    /// <summary>
    /// Whether to use managed identity authentication
    /// </summary>
    public bool UseManagedIdentity { get; set; }

    /// <summary>
    /// Retry policy for transient failures
    /// </summary>
    public RetryPolicy RetryPolicy { get; set; } = new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(60),
        UseExponentialBackoff = true,
        AttemptTimeout = TimeSpan.FromSeconds(60)
    };

    /// <summary>
    /// Transport type (AMQP or WebSockets)
    /// </summary>
    public AzureServiceBusTransportType TransportType { get; set; } = AzureServiceBusTransportType.Amqp;

    /// <summary>
    /// Maximum message size in bytes
    /// </summary>
    public long MaxMessageSizeInBytes { get; set; } = 256 * 1024; // 256 KB for Standard tier
}

/// <summary>
/// Azure Service Bus transport type
/// </summary>
public enum AzureServiceBusTransportType
{
    Amqp,
    AmqpWebSockets
}

/// <summary>
/// Options for Amazon SQS/SNS transport
/// </summary>
public class AmazonSqsTransportOptions : TransportOptions
{
    public AmazonSqsTransportOptions()
    {
        Name = "AmazonSqs";
    }

    /// <summary>
    /// AWS region
    /// </summary>
    public required string Region { get; set; }

    /// <summary>
    /// AWS access key (if not using IAM roles)
    /// </summary>
    public string? AccessKey { get; set; }

    /// <summary>
    /// AWS secret key (if not using IAM roles)
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// Whether to use IAM roles for authentication
    /// </summary>
    public bool UseIamRole { get; set; } = true;

    /// <summary>
    /// Queue name prefix
    /// </summary>
    public string? QueueNamePrefix { get; set; }

    /// <summary>
    /// Topic name prefix
    /// </summary>
    public string? TopicNamePrefix { get; set; }

    /// <summary>
    /// Whether to use FIFO queues by default
    /// </summary>
    public bool UseFifoQueues { get; set; }

    /// <summary>
    /// Maximum number of messages to receive in a single batch
    /// </summary>
    public int MaxNumberOfMessages { get; set; } = 10;

    /// <summary>
    /// Message wait time for long polling
    /// </summary>
    public TimeSpan WaitTimeSeconds { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Visibility timeout
    /// </summary>
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Options for Apache Kafka transport
/// </summary>
public class KafkaTransportOptions : TransportOptions
{
    public KafkaTransportOptions()
    {
        Name = "Kafka";
    }

    /// <summary>
    /// Bootstrap servers (comma-separated list)
    /// </summary>
    public required string BootstrapServers { get; set; }

    /// <summary>
    /// Consumer group ID
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// SASL mechanism (if authentication is required)
    /// </summary>
    public string? SaslMechanism { get; set; }

    /// <summary>
    /// SASL username
    /// </summary>
    public string? SaslUsername { get; set; }

    /// <summary>
    /// SASL password
    /// </summary>
    public string? SaslPassword { get; set; }

    /// <summary>
    /// Security protocol
    /// </summary>
    public KafkaSecurityProtocol SecurityProtocol { get; set; } = KafkaSecurityProtocol.Plaintext;

    /// <summary>
    /// Auto offset reset policy
    /// </summary>
    public KafkaAutoOffsetReset AutoOffsetReset { get; set; } = KafkaAutoOffsetReset.Latest;

    /// <summary>
    /// Enable auto commit
    /// </summary>
    public bool EnableAutoCommit { get; set; } = false;

    /// <summary>
    /// Producer acknowledgment level
    /// </summary>
    public KafkaAcks Acks { get; set; } = KafkaAcks.All;

    /// <summary>
    /// Compression type
    /// </summary>
    public KafkaCompressionType CompressionType { get; set; } = KafkaCompressionType.None;

    /// <summary>
    /// Maximum batch size in bytes
    /// </summary>
    public int BatchSize { get; set; } = 16384;

    /// <summary>
    /// Linger time before sending a batch
    /// </summary>
    public TimeSpan LingerMs { get; set; } = TimeSpan.Zero;
}

public enum KafkaSecurityProtocol
{
    Plaintext,
    Ssl,
    SaslPlaintext,
    SaslSsl
}

public enum KafkaAutoOffsetReset
{
    Latest,
    Earliest
}

public enum KafkaAcks
{
    None = 0,
    Leader = 1,
    All = -1
}

public enum KafkaCompressionType
{
    None,
    Gzip,
    Snappy,
    Lz4,
    Zstd
}
