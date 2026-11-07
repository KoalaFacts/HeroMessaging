using HeroMessaging.Abstractions;

namespace HeroMessaging.Utilities;

/// <summary>
/// Validates where an outbox entry is delivered, so every way of storing an entry
/// (the outbox processor and storage used inside a business transaction) rejects the same invalid options.
/// </summary>
public static class OutboxAddressValidator
{
    /// <summary>
    /// Returns whether the entry is delivered through a transport (to a queue or a topic) rather than in process.
    /// </summary>
    /// <param name="options">The outbox options.</param>
    public static bool IsExternal(OutboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Destination is not null || options.Topic is not null;
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when <paramref name="options"/> names both a queue and a topic,
    /// a blank queue or topic, or a routing key without a topic.
    /// </summary>
    /// <param name="options">The outbox options to validate.</param>
    /// <exception cref="ArgumentException">The delivery address is invalid; the message says how to fix it.</exception>
    public static void Validate(OutboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Destination is not null && options.Topic is not null)
            throw new ArgumentException("An outbox entry is either sent to a queue (Destination) or published to a topic (Topic), not both. Remove one of them.", nameof(options));
        if (options.RoutingKey is not null && options.Topic is null)
            throw new ArgumentException("RoutingKey applies only to an outbox entry published to a Topic. Set Topic or remove RoutingKey.", nameof(options));
        if (options.Destination is not null && string.IsNullOrWhiteSpace(options.Destination))
            throw new ArgumentException("External outbox destination must name a queue.", nameof(options));
        if (options.Topic is not null && string.IsNullOrWhiteSpace(options.Topic))
            throw new ArgumentException("External outbox topic must name a topic (for RabbitMQ, an exchange).", nameof(options));
    }
}
