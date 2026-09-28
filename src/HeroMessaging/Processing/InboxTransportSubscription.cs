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
            {
                await context.DeadLetterAsync("Invalid transport message ID", token).ConfigureAwait(false);
                return;
            }
            if (!string.Equals(envelope.ContentType, serializer.ContentType, StringComparison.OrdinalIgnoreCase))
            {
                await context.DeadLetterAsync("Unexpected content type", token).ConfigureAwait(false);
                return;
            }

            TMessage? message;
            try
            {
                message = await serializer.DeserializeAsync<TMessage>(envelope.Body.ToArray(), token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await context.DeadLetterAsync("Invalid message payload", token).ConfigureAwait(false);
                return;
            }

            if (message is null || message.MessageId != messageId)
            {
                await context.DeadLetterAsync("Transport and message body IDs do not match", token).ConfigureAwait(false);
                return;
            }

            await inbox.ProcessIncomingAsync(message, inboxOptions, token).ConfigureAwait(false);
            await context.AcknowledgeAsync(token).ConfigureAwait(false);
        }, options, cancellationToken);
    }
}
