namespace HeroMessaging.Abstractions.Transport;

/// <summary>
/// A transport that confirms both broker acceptance and routing of a published message to at least one subscriber queue.
/// </summary>
public interface IConfirmedPublishTransport : IMessageTransport
{
    /// <summary>
    /// Publish a message to a topic and complete only when the broker confirms that at least one bound queue accepted it.
    /// </summary>
    /// <exception cref="PublishRouteException">No queue bound to <paramref name="topic"/> matched the message's routing key.</exception>
    Task PublishConfirmedAsync(TransportAddress topic, TransportEnvelope envelope, CancellationToken cancellationToken = default);
}
