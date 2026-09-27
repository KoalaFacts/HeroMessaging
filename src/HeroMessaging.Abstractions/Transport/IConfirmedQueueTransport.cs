namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// A queue transport that confirms both broker acceptance and routing to a queue.
/// </summary>
public interface IConfirmedQueueTransport : IMessageTransport
{
    /// <summary>Send a message only when the broker confirms a routable queue delivery.</summary>
    Task SendConfirmedAsync(TransportAddress destination, TransportEnvelope envelope, CancellationToken cancellationToken = default);
}
