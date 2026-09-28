using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Transport;

namespace HeroMessaging.Processing;

/// <summary>
/// Subscribes a transport queue to a durable, idempotent inbox.
/// </summary>
public static class InboxTransportSubscription
{
    /// <summary>
    /// Acknowledges a delivery only after the inbox write (and any enclosing transaction) commits.
    /// The inbox processor must use durable storage and be started so persisted entries can be processed.
    /// </summary>
    public static Task<ITransportConsumer> SubscribeAsync<TMessage>(
        IMessageTransport transport,
        TransportAddress source,
        IInboxProcessor inbox,
        IMessageSerializer serializer,
        InboxOptions? inboxOptions = null,
        ConsumerOptions? consumerOptions = null,
        CancellationToken cancellationToken = default)
        where TMessage : class, IMessage
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(serializer);

        inboxOptions ??= new InboxOptions();
        if (!inboxOptions.RequireIdempotency)
            throw new ArgumentException("Inbox transport subscriptions require idempotency.", nameof(inboxOptions));

        var options = (consumerOptions ?? ConsumerOptions.Default) with
        {
            AutoAcknowledge = false,
            RequeueOnFailure = true
        };

        return transport.SubscribeAsync(source, async (envelope, context, token) =>
        {
            if (!Guid.TryParse(envelope.MessageId, out var messageId))
                throw new InvalidDataException("Transport message ID must be a GUID.");
            if (!string.Equals(envelope.ContentType, serializer.ContentType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unexpected content type: {envelope.ContentType}.");

            var message = await serializer.DeserializeAsync<TMessage>(envelope.Body.ToArray(), token).ConfigureAwait(false);
            if (message is null || message.MessageId != messageId)
                throw new InvalidDataException("Transport and message body IDs do not match.");

            await inbox.ProcessIncomingAsync(message, inboxOptions, token).ConfigureAwait(false);
            await context.AcknowledgeAsync(token).ConfigureAwait(false);
        }, options, cancellationToken);
    }
}
