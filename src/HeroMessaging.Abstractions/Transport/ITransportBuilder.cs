namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Builder interface for configuring message transports
/// Provides fluent API for transport configuration
/// </summary>
public interface ITransportBuilder
{
    /// <summary>
    /// Configure transport options
    /// </summary>
    ITransportBuilder WithOptions(Action<TransportOptionsBase> configure);

    /// <summary>
    /// Configure topology
    /// </summary>
    ITransportBuilder WithTopology(Action<ITopologyBuilder> configure);

    /// <summary>
    /// Configure default consumer options
    /// </summary>
    ITransportBuilder WithDefaultConsumerOptions(Action<ConsumerOptions> configure);

    /// <summary>
    /// Build and register the transport
    /// </summary>
    void Build();
}

/// <summary>
/// Builder interface for specific transport types
/// </summary>
/// <typeparam name="TOptions">Transport options type</typeparam>
public interface ITransportBuilder<TOptions> : ITransportBuilder
    where TOptions : TransportOptionsBase
{
    /// <summary>
    /// Configure transport-specific options
    /// </summary>
    ITransportBuilder<TOptions> WithOptions(Action<TOptions> configure);

    /// <summary>
    /// Get the current options
    /// </summary>
    TOptions Options { get; }
}

/// <summary>
/// Extension methods for transport configuration
/// </summary>
public static class TransportBuilderExtensions
{
    /// <summary>
    /// Configure RabbitMQ transport
    /// </summary>
    public static ITransportBuilder WithRabbitMq(
        this ITransportConfiguration configuration,
        string host,
        Action<RabbitMqTransportOptions>? configure = null)
    {
        var options = new RabbitMqTransportOptions { Host = host };
        configure?.Invoke(options);
        return configuration.AddTransport(options);
    }

    /// <summary>
    /// Configure Azure Service Bus transport
    /// </summary>
    public static ITransportBuilder WithAzureServiceBus(
        this ITransportConfiguration configuration,
        string connectionString,
        Action<AzureServiceBusTransportOptions>? configure = null)
    {
        var options = new AzureServiceBusTransportOptions { ConnectionString = connectionString };
        configure?.Invoke(options);
        return configuration.AddTransport(options);
    }

    /// <summary>
    /// Configure Azure Service Bus with managed identity
    /// </summary>
    public static ITransportBuilder WithAzureServiceBusManagedIdentity(
        this ITransportConfiguration configuration,
        string fullyQualifiedNamespace,
        Action<AzureServiceBusTransportOptions>? configure = null)
    {
        var options = new AzureServiceBusTransportOptions
        {
            FullyQualifiedNamespace = fullyQualifiedNamespace,
            UseManagedIdentity = true
        };
        configure?.Invoke(options);
        return configuration.AddTransport(options);
    }

    /// <summary>
    /// Configure Amazon SQS/SNS transport
    /// </summary>
    public static ITransportBuilder WithAmazonSqs(
        this ITransportConfiguration configuration,
        string region,
        Action<AmazonSqsTransportOptions>? configure = null)
    {
        var options = new AmazonSqsTransportOptions { Region = region };
        configure?.Invoke(options);
        return configuration.AddTransport(options);
    }

    /// <summary>
    /// Configure Apache Kafka transport
    /// </summary>
    public static ITransportBuilder WithKafka(
        this ITransportConfiguration configuration,
        string bootstrapServers,
        Action<KafkaTransportOptions>? configure = null)
    {
        var options = new KafkaTransportOptions { BootstrapServers = bootstrapServers };
        configure?.Invoke(options);
        return configuration.AddTransport(options);
    }

    /// <summary>
    /// Configure in-memory transport
    /// </summary>
    public static ITransportBuilder WithInMemoryTransport(
        this ITransportConfiguration configuration,
        Action<InMemoryTransportOptions>? configure = null)
    {
        var options = new InMemoryTransportOptions();
        configure?.Invoke(options);
        return configuration.AddTransport(options);
    }
}

/// <summary>
/// Interface for transport configuration
/// </summary>
public interface ITransportConfiguration
{
    /// <summary>
    /// Add a transport with the specified options
    /// </summary>
    ITransportBuilder AddTransport(TransportOptionsBase options);

    /// <summary>
    /// Set the default transport
    /// </summary>
    ITransportConfiguration UseDefaultTransport(string transportName);
}
