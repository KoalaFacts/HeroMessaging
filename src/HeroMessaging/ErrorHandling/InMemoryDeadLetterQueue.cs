using System.Collections.Concurrent;
using HeroMessaging.Abstractions.ErrorHandling;
using HeroMessaging.Abstractions.Messages;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.ErrorHandling;

public class InMemoryDeadLetterQueue(ILogger<InMemoryDeadLetterQueue> logger, TimeProvider timeProvider) : IDeadLetterQueue
{
    private readonly ConcurrentDictionary<string, object> _deadLetters = new();
    private readonly ILogger<InMemoryDeadLetterQueue> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public Task<string> SendToDeadLetterAsync<T>(T message, DeadLetterContext context, CancellationToken cancellationToken = default) where T : IMessage
    {
        var entry = new DeadLetterEntry<T>
        {
            Id = Guid.NewGuid().ToString(),
            Message = message,
            Context = context,
            CreatedAt = _timeProvider.GetUtcNow(),
            Status = DeadLetterStatus.Active
        };

        _deadLetters[entry.Id] = entry;

        _logger.LogWarning("Message {MessageId} sent to dead letter queue. Reason: {Reason}",
            message.MessageId, context.Reason);

        return Task.FromResult(entry.Id);
    }

    public Task<IEnumerable<DeadLetterEntry<T>>> GetDeadLettersAsync<T>(int limit = 100, CancellationToken cancellationToken = default) where T : IMessage
    {
        var entries = _deadLetters.Values
            .OfType<DeadLetterEntry<T>>()
            .Where(e => e.Status == DeadLetterStatus.Active)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit);

        return Task.FromResult(entries);
    }

    public Task<bool> RetryAsync<T>(string deadLetterId, CancellationToken cancellationToken = default) where T : IMessage
    {
        if (_deadLetters.TryGetValue(deadLetterId, out var entry))
        {
            if (entry is DeadLetterEntry<T> typedEntry)
            {
                var updatedEntry = typedEntry with
                {
                    Status = DeadLetterStatus.Retried,
                    RetriedAt = _timeProvider.GetUtcNow()
                };
                _deadLetters[deadLetterId] = updatedEntry;

                _logger.LogInformation("Dead letter entry {DeadLetterId} marked for retry", deadLetterId);
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    public Task<bool> DiscardAsync<T>(string deadLetterId, CancellationToken cancellationToken = default) where T : IMessage
    {
        if (_deadLetters.TryGetValue(deadLetterId, out var entry))
        {
            if (entry is DeadLetterEntry<T> typedEntry)
            {
                var updatedEntry = typedEntry with
                {
                    Status = DeadLetterStatus.Discarded,
                    DiscardedAt = _timeProvider.GetUtcNow()
                };
                _deadLetters[deadLetterId] = updatedEntry;

                _logger.LogInformation("Dead letter entry {DeadLetterId} discarded", deadLetterId);
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    public Task<long> GetDeadLetterCountAsync(CancellationToken cancellationToken = default)
    {
        var count = _deadLetters.Values
            .Cast<IDeadLetterEntry>()
            .Count(e => e.Status == DeadLetterStatus.Active);

        return Task.FromResult((long)count);
    }

    public Task<DeadLetterStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var allEntries = _deadLetters.Values.Cast<IDeadLetterEntry>().ToList();

        var countByComponent = allEntries
            .GroupBy(e => e.Context.Component)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        var countByReason = allEntries
            .GroupBy(e => e.Context.Reason.Length > 50
                ? e.Context.Reason.Substring(0, 50) + "..."
                : e.Context.Reason)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        var stats = new DeadLetterStatistics
        {
            TotalCount = allEntries.Count,
            ActiveCount = allEntries.Count(e => e.Status == DeadLetterStatus.Active),
            RetriedCount = allEntries.Count(e => e.Status == DeadLetterStatus.Retried),
            DiscardedCount = allEntries.Count(e => e.Status == DeadLetterStatus.Discarded),
            CountByComponent = countByComponent,
            CountByReason = countByReason,
            OldestEntry = allEntries.Any() ? allEntries.Min(e => e.CreatedAt) : null,
            NewestEntry = allEntries.Any() ? allEntries.Max(e => e.CreatedAt) : null
        };

        return Task.FromResult(stats);
    }
}