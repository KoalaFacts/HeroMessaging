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
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
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
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RabbitMqTransportOptions()
    {
        Name = "RabbitMQ";
        Host = string.Empty;
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

    /// <summary>
    /// Maximum number of channels per connection (default: 50)
    /// </summary>
    public int MaxChannelsPerConnection { get; set; } = 50;

    /// <summary>
    /// Channel lifetime before expiration and recreation (default: 5 minutes)
    /// </summary>
    public TimeSpan ChannelLifetime { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Options for Azure Service Bus transport
/// </summary>
public class AzureServiceBusTransportOptions : TransportOptions
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
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
/// Specifies the transport protocol used for Azure Service Bus connections.
/// </summary>
/// <remarks>
/// The choice of transport type affects:
/// - Network compatibility (firewall/proxy)
/// - Connection establishment performance
/// - Protocol overhead
///
/// Use AMQP for best performance in environments that allow outbound TCP connections.
/// Use WebSockets when behind restrictive firewalls or proxies that only allow HTTP/HTTPS.
/// </remarks>
public enum AzureServiceBusTransportType
{
    /// <summary>
    /// Uses AMQP 1.0 protocol over TCP (default).
    /// Provides the best performance and lowest latency.
    /// </summary>
    /// <remarks>
    /// - Default port: 5671 (AMQP with TLS)
    /// - Best performance with lowest protocol overhead
    /// - Requires outbound TCP connectivity
    /// - Recommended for production environments
    /// - May be blocked by strict corporate firewalls
    /// </remarks>
    Amqp,

    /// <summary>
    /// Uses AMQP 1.0 protocol over WebSockets (HTTP/HTTPS).
    /// Provides compatibility with restrictive network environments.
    /// </summary>
    /// <remarks>
    /// - Port: 443 (HTTPS)
    /// - Works through most firewalls and proxies
    /// - Slightly higher overhead than native AMQP
    /// - Recommended when AMQP port 5671 is blocked
    /// - Useful in browser-based or restricted network scenarios
    ///
    /// Example configuration:
    /// <code>
    /// services.AddHeroMessaging(builder =>
    /// {
    ///     builder.AddTransport&lt;AzureServiceBusTransport&gt;(options =>
    ///     {
    ///         options.ConnectionString = "Endpoint=sb://...";
    ///         options.TransportType = AzureServiceBusTransportType.AmqpWebSockets;
    ///     });
    /// });
    /// </code>
    /// </remarks>
    AmqpWebSockets
}

/// <summary>
/// Options for Amazon SQS/SNS transport
/// </summary>
public class AmazonSqsTransportOptions : TransportOptions
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public AmazonSqsTransportOptions()
    {
        Name = "AmazonSqs";
        Region = string.Empty;
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
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public KafkaTransportOptions()
    {
        Name = "Kafka";
        BootstrapServers = string.Empty;
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

/// <summary>
/// Specifies the security protocol used for Kafka broker connections.
/// </summary>
/// <remarks>
/// The security protocol determines how the client communicates with Kafka brokers:
/// - Encryption (SSL/TLS)
/// - Authentication (SASL)
/// - Both or neither
///
/// Choose the appropriate protocol based on your security requirements and broker configuration.
/// Production environments should use SSL or SASL+SSL for secure communication.
/// </remarks>
public enum KafkaSecurityProtocol
{
    /// <summary>
    /// Unencrypted, unauthenticated connection (default for development).
    /// </summary>
    /// <remarks>
    /// - No encryption (plaintext communication)
    /// - No authentication required
    /// - Fastest performance, lowest overhead
    /// - NOT recommended for production
    /// - Suitable for local development and testing
    /// - Default port: 9092
    ///
    /// Security risks:
    /// - All traffic is visible on the network
    /// - No protection against man-in-the-middle attacks
    /// - Anyone can produce/consume messages
    /// </remarks>
    Plaintext,

    /// <summary>
    /// SSL/TLS encrypted connection without SASL authentication.
    /// </summary>
    /// <remarks>
    /// - Encrypted communication using SSL/TLS
    /// - Authentication via SSL certificates (optional)
    /// - Protects data in transit
    /// - Moderate overhead for encryption
    /// - Default port: 9093
    ///
    /// Use when:
    /// - You need encryption but not SASL authentication
    /// - You have SSL certificate infrastructure
    /// - Client authentication via certificates is acceptable
    ///
    /// Configuration requirements:
    /// - Broker must have SSL enabled
    /// - Client may need truststore for server verification
    /// - Client may need keystore for client authentication
    /// </remarks>
    Ssl,

    /// <summary>
    /// SASL authentication over plaintext (no encryption).
    /// </summary>
    /// <remarks>
    /// - Authentication using SASL mechanisms (PLAIN, SCRAM, GSSAPI)
    /// - No encryption (credentials sent in plaintext!)
    /// - NOT recommended for production
    /// - Useful for testing SASL configuration
    /// - Default port: 9092
    ///
    /// Security risks:
    /// - Credentials transmitted in plaintext
    /// - Message content not encrypted
    /// - Vulnerable to network sniffing
    ///
    /// Only use in secure, isolated networks for development/testing.
    /// </remarks>
    SaslPlaintext,

    /// <summary>
    /// SASL authentication over SSL/TLS encrypted connection (recommended for production).
    /// </summary>
    /// <remarks>
    /// - Encrypted communication using SSL/TLS
    /// - Authentication using SASL mechanisms
    /// - Highest security level
    /// - Recommended for production environments
    /// - Default port: 9094
    ///
    /// Supported SASL mechanisms:
    /// - PLAIN: Username/password (simple but requires SSL)
    /// - SCRAM-SHA-256/512: Salted challenge-response
    /// - GSSAPI (Kerberos): Enterprise authentication
    /// - OAUTHBEARER: OAuth 2.0 tokens
    ///
    /// Example configuration:
    /// <code>
    /// services.AddHeroMessaging(builder =>
    /// {
    ///     builder.AddTransport&lt;KafkaTransport&gt;(options =>
    ///     {
    ///         options.BootstrapServers = "kafka.example.com:9094";
    ///         options.SecurityProtocol = KafkaSecurityProtocol.SaslSsl;
    ///         options.SaslMechanism = "SCRAM-SHA-256";
    ///         options.SaslUsername = "myapp";
    ///         options.SaslPassword = "secret";
    ///     });
    /// });
    /// </code>
    /// </remarks>
    SaslSsl
}

/// <summary>
/// Specifies where a Kafka consumer should start reading when no committed offset exists.
/// </summary>
/// <remarks>
/// This setting only applies when:
/// - The consumer group is new (no previous offset committed)
/// - The committed offset is invalid or out of range
/// - The consumer is reading a partition for the first time
///
/// Once an offset is committed, the consumer will always resume from that offset,
/// regardless of this setting.
/// </remarks>
public enum KafkaAutoOffsetReset
{
    /// <summary>
    /// Start consuming from the latest offset (end of topic).
    /// Only new messages produced after the consumer starts will be consumed.
    /// </summary>
    /// <remarks>
    /// - Consumer will only see new messages
    /// - Existing messages in the topic are skipped
    /// - Useful for real-time event processing
    /// - Prevents processing historical data on first start
    /// - Default for most real-time applications
    ///
    /// Use when:
    /// - You only care about new events
    /// - Processing historical data would be problematic
    /// - Starting fresh is acceptable
    ///
    /// Example:
    /// <code>
    /// options.AutoOffsetReset = KafkaAutoOffsetReset.Latest;
    /// // Consumer group "myapp" starts fresh
    /// // Only messages produced after consumer starts are processed
    /// </code>
    /// </remarks>
    Latest,

    /// <summary>
    /// Start consuming from the earliest offset (beginning of topic).
    /// All available messages in the topic will be consumed from the start.
    /// </summary>
    /// <remarks>
    /// - Consumer will process all messages from the beginning
    /// - Useful for data reprocessing and catch-up scenarios
    /// - Can cause long startup times with large topics
    /// - Ensures no messages are missed
    ///
    /// Use when:
    /// - You need to process all historical data
    /// - Building materialized views or projections
    /// - Data reprocessing is required
    /// - No messages should be skipped
    ///
    /// Warning:
    /// - May consume a large number of messages on first start
    /// - Can delay consumer readiness
    /// - Consider topic retention settings
    ///
    /// Example:
    /// <code>
    /// options.AutoOffsetReset = KafkaAutoOffsetReset.Earliest;
    /// // Consumer group "analytics" processes all historical data
    /// // Useful for building dashboards from historical events
    /// </code>
    /// </remarks>
    Earliest
}

/// <summary>
/// Specifies the acknowledgment mode for Kafka producers.
/// Controls durability and performance trade-offs.
/// </summary>
/// <remarks>
/// The acks setting determines how many broker acknowledgments the producer requires
/// before considering a message successfully sent. Higher values provide better
/// durability at the cost of latency and throughput.
///
/// Consider your durability requirements, acceptable data loss risk, and performance needs
/// when choosing the acks level.
/// </remarks>
public enum KafkaAcks
{
    /// <summary>
    /// No acknowledgment required (fire and forget).
    /// Provides highest throughput but no delivery guarantees.
    /// </summary>
    /// <remarks>
    /// - Producer does not wait for any acknowledgment
    /// - Highest throughput, lowest latency
    /// - NO durability guarantees
    /// - Messages may be lost without notice
    ///
    /// Durability: NONE
    /// - Message may be lost if broker fails
    /// - Message may be lost during network issues
    /// - No way to detect message loss
    ///
    /// Use when:
    /// - Maximum throughput is critical
    /// - Message loss is acceptable
    /// - Data is non-critical (metrics, logs)
    /// - Low latency is more important than reliability
    ///
    /// NOT recommended for:
    /// - Financial transactions
    /// - Critical business events
    /// - Audit logs
    /// - Any data that must not be lost
    ///
    /// Example:
    /// <code>
    /// options.Acks = KafkaAcks.None;
    /// // Suitable for high-volume metrics where occasional loss is acceptable
    /// </code>
    /// </remarks>
    None = 0,

    /// <summary>
    /// Wait for leader broker acknowledgment only.
    /// Balanced trade-off between performance and durability.
    /// </summary>
    /// <remarks>
    /// - Producer waits for leader broker to write to its log
    /// - Does not wait for replication to followers
    /// - Moderate throughput and latency
    /// - Partial durability guarantees
    ///
    /// Durability: PARTIAL
    /// - Message is durable on leader
    /// - Message may be lost if leader fails before replication
    /// - Better than None, but not guaranteed
    ///
    /// Use when:
    /// - Balance between performance and durability is needed
    /// - Some risk of message loss is acceptable
    /// - Leader failure is rare in your cluster
    ///
    /// Risk scenario:
    /// - Leader acknowledges message
    /// - Leader fails before replicating to followers
    /// - New leader elected without the message
    /// - Message is lost
    ///
    /// Example:
    /// <code>
    /// options.Acks = KafkaAcks.Leader;
    /// // Good balance for non-critical but important data
    /// </code>
    /// </remarks>
    Leader = 1,

    /// <summary>
    /// Wait for acknowledgment from all in-sync replicas (ISR).
    /// Provides strongest durability guarantees (recommended for production).
    /// </summary>
    /// <remarks>
    /// - Producer waits for leader and all in-sync replicas
    /// - Lowest throughput, highest latency
    /// - Strongest durability guarantees
    /// - Prevents message loss on leader failure
    ///
    /// Durability: MAXIMUM
    /// - Message is replicated to all in-sync replicas
    /// - Message survives leader failure
    /// - Best protection against data loss
    ///
    /// Use when:
    /// - Data must not be lost
    /// - Durability is more important than performance
    /// - Processing critical business events
    /// - Regulatory compliance requires data retention
    ///
    /// Recommended for:
    /// - Financial transactions
    /// - Order processing
    /// - Audit logs
    /// - Critical business events
    /// - Any data that cannot be recreated
    ///
    /// Performance impact:
    /// - Higher latency (waits for replication)
    /// - Lower throughput
    /// - Depends on replication factor and ISR health
    ///
    /// Requirements:
    /// - Topic must have min.insync.replicas configured (typically 2)
    /// - Replication factor should be >= min.insync.replicas
    ///
    /// Example:
    /// <code>
    /// services.AddHeroMessaging(builder =>
    /// {
    ///     builder.AddTransport&lt;KafkaTransport&gt;(options =>
    ///     {
    ///         options.BootstrapServers = "kafka:9092";
    ///         options.Acks = KafkaAcks.All; // Maximum durability
    ///     });
    /// });
    /// </code>
    /// </remarks>
    All = -1
}

/// <summary>
/// Specifies the compression algorithm used for Kafka message batches.
/// </summary>
/// <remarks>
/// Compression reduces network bandwidth and storage at the cost of CPU for
/// compression/decompression. The effectiveness depends on message content:
/// - Text/JSON: High compression ratios (often 5-10x)
/// - Binary/encrypted: Low compression ratios
/// - Already compressed: No benefit
///
/// Choose compression based on:
/// - Message content type
/// - Network bandwidth constraints
/// - CPU availability
/// - Storage costs
/// - Latency requirements
///
/// Compression is performed on the producer and decompressed on the consumer.
/// Brokers typically store messages in compressed form.
/// </remarks>
public enum KafkaCompressionType
{
    /// <summary>
    /// No compression (default).
    /// </summary>
    /// <remarks>
    /// - No CPU overhead for compression
    /// - Highest network bandwidth usage
    /// - Highest storage usage
    /// - Lowest producer/consumer latency
    ///
    /// Use when:
    /// - Messages are small
    /// - Messages are already compressed
    /// - Network bandwidth is plentiful
    /// - CPU is constrained
    /// - Lowest latency is critical
    /// </remarks>
    None,

    /// <summary>
    /// GZIP compression (highest compression ratio, highest CPU cost).
    /// </summary>
    /// <remarks>
    /// - Best compression ratio (typically 5-10x for text)
    /// - Highest CPU usage
    /// - Slowest compression/decompression
    /// - Good for text and JSON messages
    ///
    /// Characteristics:
    /// - Compression ratio: Excellent
    /// - Compression speed: Slow
    /// - Decompression speed: Moderate
    /// - CPU usage: High
    ///
    /// Use when:
    /// - Network bandwidth is expensive/limited
    /// - Storage costs are high
    /// - CPU is plentiful
    /// - Latency is not critical
    /// - Messages are highly compressible (text, JSON)
    ///
    /// Example:
    /// <code>
    /// options.CompressionType = KafkaCompressionType.Gzip;
    /// // Best for large JSON messages over slow networks
    /// </code>
    /// </remarks>
    Gzip,

    /// <summary>
    /// Snappy compression (balanced compression and speed).
    /// </summary>
    /// <remarks>
    /// - Moderate compression ratio
    /// - Fast compression/decompression
    /// - Low CPU overhead
    /// - Good general-purpose choice
    ///
    /// Characteristics:
    /// - Compression ratio: Good
    /// - Compression speed: Fast
    /// - Decompression speed: Very fast
    /// - CPU usage: Low
    ///
    /// Use when:
    /// - Need balance between compression and performance
    /// - Moderate CPU available
    /// - Latency is important
    /// - General-purpose compression needed
    ///
    /// Recommended for:
    /// - Most production workloads
    /// - Mixed message types
    /// - When in doubt, start here
    ///
    /// Example:
    /// <code>
    /// options.CompressionType = KafkaCompressionType.Snappy;
    /// // Good default for production
    /// </code>
    /// </remarks>
    Snappy,

    /// <summary>
    /// LZ4 compression (fastest, good compression).
    /// </summary>
    /// <remarks>
    /// - Good compression ratio
    /// - Very fast compression/decompression
    /// - Low CPU overhead
    /// - Excellent for high-throughput scenarios
    ///
    /// Characteristics:
    /// - Compression ratio: Good
    /// - Compression speed: Very fast
    /// - Decompression speed: Very fast
    /// - CPU usage: Very low
    ///
    /// Use when:
    /// - High throughput is required
    /// - Low latency is critical
    /// - CPU overhead must be minimized
    /// - Good compression is still desired
    ///
    /// Recommended for:
    /// - High-throughput applications
    /// - Real-time streaming
    /// - CPU-constrained environments
    /// - Latency-sensitive workloads
    ///
    /// Example:
    /// <code>
    /// options.CompressionType = KafkaCompressionType.Lz4;
    /// // Best for high-throughput, low-latency scenarios
    /// </code>
    /// </remarks>
    Lz4,

    /// <summary>
    /// Zstandard compression (best overall: excellent compression with good speed).
    /// </summary>
    /// <remarks>
    /// - Excellent compression ratio (better than Snappy/LZ4, close to Gzip)
    /// - Fast compression/decompression
    /// - Moderate CPU usage
    /// - Modern algorithm with tunable compression levels
    ///
    /// Characteristics:
    /// - Compression ratio: Excellent
    /// - Compression speed: Fast
    /// - Decompression speed: Very fast
    /// - CPU usage: Moderate
    ///
    /// Use when:
    /// - Need best compression without Gzip's CPU cost
    /// - Storage costs are significant
    /// - Network bandwidth is constrained
    /// - Modern Kafka version (0.11+)
    ///
    /// Advantages over alternatives:
    /// - Better compression than Snappy/LZ4
    /// - Faster than Gzip
    /// - Tunable compression levels
    /// - Excellent for most workloads
    ///
    /// Recommended for:
    /// - New deployments (Kafka 0.11+)
    /// - Storage-sensitive applications
    /// - Network bandwidth optimization
    /// - When you want the best overall balance
    ///
    /// Note: Requires Kafka 0.11.0 or later
    ///
    /// Example:
    /// <code>
    /// options.CompressionType = KafkaCompressionType.Zstd;
    /// // Modern choice for new deployments
    /// </code>
    /// </remarks>
    Zstd
}
