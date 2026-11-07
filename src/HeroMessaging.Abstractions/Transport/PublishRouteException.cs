namespace HeroMessaging.Abstractions.Transport;

/// <summary>A confirmed publish that no subscriber queue received.</summary>
public sealed class PublishRouteException : Exception
{
    /// <summary>Initializes a publish routing failure.</summary>
    public PublishRouteException(string topic, string routingKey, Exception innerException)
        : base($"Message published to '{topic}' with routing key '{routingKey}' was not routed to any queue. " +
               "Bind a subscriber queue to the topic for this routing key, or set the envelope's 'RoutingKey' header to a bound key.",
            innerException)
    {
        Topic = topic;
        RoutingKey = routingKey;
    }

    /// <summary>The topic (exchange) the message was published to.</summary>
    public string Topic { get; }

    /// <summary>The routing key no binding matched.</summary>
    public string RoutingKey { get; }
}
