using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

/// <summary>What one external delivery attempt achieved, for processor metrics.</summary>
internal enum ExternalDeliveryOutcome
{
    /// <summary>No send was attempted or its result could not be recorded (shutdown, lost lease).</summary>
    NotCompleted,
    /// <summary>The transport confirmed the send and this worker completed the entry under its lease.</summary>
    Delivered,
    /// <summary>The send or its preparation threw; the entry was scheduled for retry or marked failed.</summary>
    Failed
}

internal sealed class ExternalOutboxDelivery(
    IExternalOutboxStorage storage,
    IConfirmedQueueTransport transport,
    IMessageSerializer messageSerializer,
    TimeProvider timeProvider,
    ILogger logger)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromSeconds(20);

    /// <summary>Whether the transport can publish entries addressed to a topic.</summary>
    public bool CanPublish => transport is IConfirmedPublishTransport;

    private Task SendAsync(OutboxOptions options, TransportEnvelope envelope, CancellationToken cancellationToken)
    {
        if (options.Topic is null)
            return transport.SendConfirmedAsync(TransportAddress.Queue(options.Destination!), envelope, cancellationToken);

        if (transport is not IConfirmedPublishTransport publisher)
            throw new NotSupportedException($"Outbox entry is addressed to topic '{options.Topic}', but the transport does not implement {nameof(IConfirmedPublishTransport)}.");
        if (options.RoutingKey is not null)
            envelope = envelope.WithHeader("RoutingKey", options.RoutingKey);
        return publisher.PublishConfirmedAsync(TransportAddress.Topic(options.Topic), envelope, cancellationToken);
    }

    public async Task<ExternalDeliveryOutcome> DeliverAsync(OutboxEntry entry, Guid token, CancellationToken shutdown)
    {
        if (shutdown.IsCancellationRequested)
        {
            if (!await storage.RetryExternalAsync(entry.Id, token, entry.RetryCount, timeProvider.GetUtcNow(),
                    "Delivery interrupted by shutdown").ConfigureAwait(false))
                logger.LogWarning("Outbox lease for {EntryId} was lost during shutdown", entry.Id);
            return ExternalDeliveryOutcome.NotCompleted;
        }

        if (!await storage.RenewExternalAsync(entry.Id, token, LeaseDuration).ConfigureAwait(false))
        {
            logger.LogWarning("Outbox lease for {EntryId} was lost before delivery started", entry.Id);
            return ExternalDeliveryOutcome.NotCompleted;
        }

        using var delivery = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        var renewal = RenewLeaseAsync(entry.Id, token, delivery);
        try
        {
            var message = entry.Message;
            var envelope = new TransportEnvelope
            {
                MessageId = message.MessageId.ToString(),
                MessageType = message.GetType().AssemblyQualifiedName ?? message.GetType().FullName ?? message.GetType().Name,
                CorrelationId = message.CorrelationId,
                CausationId = message.CausationId,
                Timestamp = message.Timestamp,
                ContentType = messageSerializer.ContentType,
                Body = await SerializeConcreteAsync(message, delivery.Token).ConfigureAwait(false)
            };

            if (transport.State != TransportState.Connected)
                await transport.ConnectAsync(delivery.Token).ConfigureAwait(false);
            await SendAsync(entry.Options, envelope, delivery.Token).ConfigureAwait(false);

            await delivery.CancelAsync().ConfigureAwait(false);
            await renewal.ConfigureAwait(false);
            if (await storage.CompleteExternalAsync(entry.Id, token).ConfigureAwait(false))
                return ExternalDeliveryOutcome.Delivered;
            logger.LogWarning("Outbox lease for {EntryId} expired after transport delivery; a duplicate may be retried", entry.Id);
            return ExternalDeliveryOutcome.NotCompleted;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            await delivery.CancelAsync().ConfigureAwait(false);
            await renewal.ConfigureAwait(false);
            if (!await storage.RetryExternalAsync(entry.Id, token, entry.RetryCount, timeProvider.GetUtcNow(),
                    "Delivery interrupted by shutdown").ConfigureAwait(false))
                logger.LogWarning("Outbox lease for {EntryId} was lost during shutdown", entry.Id);
            return ExternalDeliveryOutcome.NotCompleted;
        }
        catch (Exception ex)
        {
            await delivery.CancelAsync().ConfigureAwait(false);
            await renewal.ConfigureAwait(false);
            logger.LogError(ex, "External outbox delivery failed for {EntryId}", entry.Id);

            var retryCount = entry.RetryCount + 1;
            bool updated;
            try
            {
                updated = retryCount >= entry.Options.MaxRetries
                    ? await storage.FailExternalAsync(entry.Id, token, retryCount, ex.Message).ConfigureAwait(false)
                    : await storage.RetryExternalAsync(entry.Id, token, retryCount,
                        timeProvider.GetUtcNow().Add(entry.Options.RetryDelay ?? RetryDelayCalculator.CalculateWithoutJitter(retryCount)), ex.Message).ConfigureAwait(false);
            }
            catch (Exception recordError)
            {
                // The send still failed: report it as a failed attempt even though its retry state was not stored.
                logger.LogError(recordError, "External outbox state could not be updated for {EntryId}; its lease will expire for retry", entry.Id);
                return ExternalDeliveryOutcome.Failed;
            }
            if (!updated)
                logger.LogWarning("Outbox lease for {EntryId} was lost before failure could be recorded", entry.Id);
            return ExternalDeliveryOutcome.Failed;
        }
        finally
        {
            await delivery.CancelAsync().ConfigureAwait(false);
            await renewal.ConfigureAwait(false);
        }
    }

    private ValueTask<byte[]> SerializeConcreteAsync(IMessage message, CancellationToken cancellationToken)
        => messageSerializer.SerializeAsync((dynamic)message, cancellationToken);

    private async Task RenewLeaseAsync(string entryId, Guid token, CancellationTokenSource delivery)
    {
        try
        {
            while (!delivery.IsCancellationRequested)
            {
                await Task.Delay(LeaseRenewalInterval, timeProvider, delivery.Token).ConfigureAwait(false);
                if (!await storage.RenewExternalAsync(entryId, token, LeaseDuration, delivery.Token).ConfigureAwait(false))
                {
                    logger.LogWarning("Outbox lease for {EntryId} could not be renewed", entryId);
                    await delivery.CancelAsync().ConfigureAwait(false);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (delivery.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Outbox lease renewal failed for {EntryId}", entryId);
            await delivery.CancelAsync().ConfigureAwait(false);
        }
    }
}
