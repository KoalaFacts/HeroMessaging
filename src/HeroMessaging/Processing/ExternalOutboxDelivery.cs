using System.Text;
using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

internal sealed class ExternalOutboxDelivery(
    IExternalOutboxStorage storage,
    IConfirmedQueueTransport transport,
    IJsonSerializer jsonSerializer,
    TimeProvider timeProvider,
    ILogger logger)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LeaseRenewalInterval = TimeSpan.FromSeconds(20);

    public async Task DeliverAsync(OutboxEntry entry, Guid token)
    {
        if (!await storage.RenewExternalAsync(entry.Id, token, LeaseDuration).ConfigureAwait(false))
        {
            logger.LogWarning("Outbox lease for {EntryId} was lost before delivery started", entry.Id);
            return;
        }

        using var delivery = new CancellationTokenSource();
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
                ContentType = "application/json",
                Body = Encoding.UTF8.GetBytes(jsonSerializer.SerializeToString((object)message))
            };

            if (transport.State != TransportState.Connected)
                await transport.ConnectAsync(delivery.Token).ConfigureAwait(false);
            await transport.SendConfirmedAsync(TransportAddress.Queue(entry.Options.Destination!), envelope, delivery.Token).ConfigureAwait(false);

            await delivery.CancelAsync().ConfigureAwait(false);
            await renewal.ConfigureAwait(false);
            if (!await storage.CompleteExternalAsync(entry.Id, token).ConfigureAwait(false))
                logger.LogWarning("Outbox lease for {EntryId} expired after transport delivery; a duplicate may be retried", entry.Id);
        }
        catch (Exception ex)
        {
            await delivery.CancelAsync().ConfigureAwait(false);
            await renewal.ConfigureAwait(false);
            logger.LogError(ex, "External outbox delivery failed for {EntryId}", entry.Id);

            var retryCount = entry.RetryCount + 1;
            var updated = retryCount >= entry.Options.MaxRetries
                ? await storage.FailExternalAsync(entry.Id, token, retryCount, ex.Message).ConfigureAwait(false)
                : await storage.RetryExternalAsync(entry.Id, token, retryCount,
                    timeProvider.GetUtcNow().Add(entry.Options.RetryDelay ?? RetryDelayCalculator.CalculateWithoutJitter(retryCount)), ex.Message).ConfigureAwait(false);
            if (!updated)
                logger.LogWarning("Outbox lease for {EntryId} was lost before failure could be recorded", entry.Id);
        }
        finally
        {
            await delivery.CancelAsync().ConfigureAwait(false);
            await renewal.ConfigureAwait(false);
        }
    }

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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Outbox lease renewal failed for {EntryId}", entryId);
            await delivery.CancelAsync().ConfigureAwait(false);
        }
    }
}
