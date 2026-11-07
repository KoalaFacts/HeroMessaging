using HeroMessaging.Abstractions.Transport;

namespace HeroMessaging.Processing;

/// <summary>
/// Reads an external outbox destination (<c>OutboxOptions.Destination</c>): <c>topic:&lt;name&gt;</c> addresses a topic,
/// <c>queue:&lt;name&gt;</c> or a bare name addresses a queue.
/// </summary>
internal static class OutboxDestination
{
    private const string TopicPrefix = "topic:";
    private const string QueuePrefix = "queue:";

    public static TransportAddress Parse(string destination)
    {
        if (destination.StartsWith(TopicPrefix, StringComparison.Ordinal))
            return TransportAddress.Topic(RequireName(destination, TopicPrefix));
        if (destination.StartsWith(QueuePrefix, StringComparison.Ordinal))
            return TransportAddress.Queue(RequireName(destination, QueuePrefix));
        if (string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("External outbox destination must name a queue or, with the 'topic:' prefix, a topic.", nameof(destination));
        return TransportAddress.Queue(destination);
    }

    public static NotSupportedException TopicNotSupported(TransportAddress topic)
        => new($"External outbox destination 'topic:{topic.Name}' needs a transport that implements {nameof(IConfirmedPublishTransport)} " +
            "(for example the RabbitMQ transport with publisher confirms). Register such a transport, or address a queue instead.");

    private static string RequireName(string destination, string prefix)
    {
        var name = destination[prefix.Length..];
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"External outbox destination '{destination}' has no name after '{prefix}'.", nameof(destination));
        return name;
    }
}
