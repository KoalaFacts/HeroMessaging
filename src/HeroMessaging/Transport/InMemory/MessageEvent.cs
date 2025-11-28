using HeroMessaging.Abstractions.Transport;
using HeroMessaging.RingBuffer.EventFactories;

namespace HeroMessaging.Transport.InMemory;

/// <summary>
/// Message wrapper for ring buffer (pre-allocated for zero-allocation scenarios).
/// </summary>
internal sealed class MessageEvent
{
    /// <summary>
    /// Gets or sets the transport envelope containing the message.
    /// </summary>
    public TransportEnvelope? Envelope { get; set; }
}

/// <summary>
/// Factory for creating pre-allocated MessageEvent instances.
/// </summary>
internal sealed class MessageEventFactory : IEventFactory<MessageEvent>
{
    /// <summary>
    /// Creates a new MessageEvent instance.
    /// </summary>
    /// <returns>A new MessageEvent.</returns>
    public MessageEvent Create() => new MessageEvent();
}
