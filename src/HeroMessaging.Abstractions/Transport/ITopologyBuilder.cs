namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// Builder for configuring transport topology
/// Provides a fluent API for defining queues, topics, exchanges, and bindings
/// </summary>
public interface ITopologyBuilder
{
    /// <summary>
    /// Define a queue
    /// </summary>
    ITopologyBuilder Queue(string name, Action<QueueDefinition>? configure = null);

    /// <summary>
    /// Define a topic
    /// </summary>
    ITopologyBuilder Topic(string name, Action<TopicDefinition>? configure = null);

    /// <summary>
    /// Define an exchange (RabbitMQ)
    /// </summary>
    ITopologyBuilder Exchange(string name, ExchangeType type, Action<ExchangeDefinition>? configure = null);

    /// <summary>
    /// Define a subscription
    /// </summary>
    ITopologyBuilder Subscription(string topicName, string subscriptionName, Action<SubscriptionDefinition>? configure = null);

    /// <summary>
    /// Bind a queue to an exchange (RabbitMQ)
    /// </summary>
    ITopologyBuilder Bind(string exchangeName, string queueName, string? routingKey = null, Action<BindingDefinition>? configure = null);

    /// <summary>
    /// Build the topology configuration
    /// </summary>
    TransportTopology Build();
}

/// <summary>
/// Default implementation of topology builder
/// </summary>
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
    /// Create a new topology builder
    /// </summary>
    public static ITopologyBuilder Create() => new TopologyBuilder();
}
