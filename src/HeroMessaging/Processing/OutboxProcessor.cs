using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Commands;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing;

public class OutboxProcessor : PollingBackgroundServiceBase<OutboxEntry>, IOutboxProcessor
{
    private readonly IOutboxStorage _outboxStorage;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;

    public OutboxProcessor(
        IOutboxStorage outboxStorage,
        IServiceProvider serviceProvider,
        ILogger<OutboxProcessor> logger,
        TimeProvider timeProvider)
        : base(logger, timeProvider, maxDegreeOfParallelism: Environment.ProcessorCount, boundedCapacity: 100)
    {
        _outboxStorage = outboxStorage;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task PublishToOutbox(IMessage message, OutboxOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new OutboxOptions();

        var entry = await _outboxStorage.AddAsync(message, options, cancellationToken);

        // Trigger immediate processing for high priority messages
        if (options.Priority > 5)
        {
            await SubmitWorkItem(entry, cancellationToken);
        }
    }

    public new bool IsRunning => base.IsRunning;

    public IOutboxProcessorMetrics GetMetrics()
    {
        return new OutboxProcessorMetrics
        {
            PendingMessages = 0, // TODO: Track metrics
            ProcessedMessages = 0,
            FailedMessages = 0,
            LastProcessedTime = _timeProvider.GetUtcNow()
        };
    }

    private class OutboxProcessorMetrics : IOutboxProcessorMetrics
    {
        public long PendingMessages { get; init; }
        public long ProcessedMessages { get; init; }
        public long FailedMessages { get; init; }
        public DateTimeOffset? LastProcessedTime { get; init; }
    }

    protected override string GetServiceName() => "Outbox processor";

    protected override async Task<IEnumerable<OutboxEntry>> PollForWorkItems(CancellationToken cancellationToken)
    {
        return await _outboxStorage.GetPendingAsync(100, cancellationToken);
    }

    protected override async Task ProcessWorkItem(OutboxEntry entry)
    {
        try
        {
            // Mark as processing to prevent duplicate processing
            entry.Status = OutboxStatus.Processing;

            // Simulate sending to external system based on destination
            if (!string.IsNullOrEmpty(entry.Options.Destination))
            {
                await SendToExternalSystem(entry);
            }
            else
            {
                // Process internally
                using var scope = _serviceProvider.CreateScope();
                var messaging = scope.ServiceProvider.GetRequiredService<IHeroMessaging>();

                switch (entry.Message)
                {
                    case ICommand command:
                        await messaging.SendAsync(command);
                        break;
                    case IEvent @event:
                        await messaging.PublishAsync(@event);
                        break;
                }
            }

            await _outboxStorage.MarkProcessedAsync(entry.Id);

            Logger.LogInformation("Outbox entry {EntryId} processed successfully", entry.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing outbox entry {EntryId}", entry.Id);

            entry.RetryCount++;

            if (entry.RetryCount >= entry.Options.MaxRetries)
            {
                await _outboxStorage.MarkFailedAsync(entry.Id, ex.Message);
                Logger.LogError("Outbox entry {EntryId} failed after {RetryCount} retries",
                    entry.Id, entry.RetryCount);
            }
            else
            {
                var delay = entry.Options.RetryDelay ?? TimeSpan.FromSeconds(Math.Pow(2, entry.RetryCount));
                var nextRetry = _timeProvider.GetUtcNow().Add(delay);

                await _outboxStorage.UpdateRetryCountAsync(entry.Id, entry.RetryCount, nextRetry);

                Logger.LogWarning("Outbox entry {EntryId} will be retried at {NextRetry} (attempt {RetryCount}/{MaxRetries})",
                    entry.Id, nextRetry, entry.RetryCount, entry.Options.MaxRetries);
            }
        }
    }

    private async Task SendToExternalSystem(OutboxEntry entry)
    {
        // This is where you would implement actual external system integration
        // For now, we'll simulate it
        Logger.LogInformation("Sending message {MessageId} to external system: {Destination}",
            entry.Message.MessageId, entry.Options.Destination);

        // Simulate network call
        await Task.Delay(100);

        // Simulate occasional failures for testing
        if (RandomHelper.Instance.Next(10) == 0)
        {
            throw new InvalidOperationException($"Failed to send to {entry.Options.Destination}");
        }
    }
}