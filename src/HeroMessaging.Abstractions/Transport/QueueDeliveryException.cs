namespace HeroMessaging.Abstractions.Transport;

/// <summary>A confirmed queue send could not reach its destination.</summary>
public sealed class QueueDeliveryException : Exception
{
    /// <summary>Initializes a queue delivery failure.</summary>
    public QueueDeliveryException(string destination, Exception innerException)
        : base($"Message could not be routed to queue '{destination}'.", innerException)
    {
        Destination = destination;
    }

    /// <summary>The queue that could not receive the message.</summary>
    public string Destination { get; }
}
