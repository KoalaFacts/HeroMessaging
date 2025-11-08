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

    public Task<string> SendToDeadLetter<T>(T message, DeadLetterContext context, CancellationToken cancellationToken = default) where T : IMessage
    {
        var entry = new DeadLetterEntry<T>
        {
            Id = Guid.NewGuid().ToString(),
            Message = message,
            Context = context,
            CreatedAt = _timeProvider.GetUtcNow().DateTime,
            Status = DeadLetterStatus.Active
        };

        _deadLetters[entry.Id] = entry;

        _logger.LogWarning("Message {MessageId} sent to dead letter queue. Reason: {Reason}",
            message.MessageId, context.Reason);

        return Task.FromResult(entry.Id);
    }

    public Task<IEnumerable<DeadLetterEntry<T>>> GetDeadLetters<T>(int limit = 100, CancellationToken cancellationToken = default) where T : IMessage
    {
        var entries = _deadLetters.Values
            .OfType<DeadLetterEntry<T>>()
            .Where(e => e.Status == DeadLetterStatus.Active)
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit);

        return Task.FromResult(entries);
    }

    public Task<bool> Retry<T>(string deadLetterId, CancellationToken cancellationToken = default) where T : IMessage
    {
        if (_deadLetters.TryGetValue(deadLetterId, out var entry))
        {
            if (entry is DeadLetterEntry<T> typedEntry)
            {
                typedEntry.Status = DeadLetterStatus.Retried;
                typedEntry.RetriedAt = _timeProvider.GetUtcNow().DateTime;

                _logger.LogInformation("Dead letter entry {DeadLetterId} marked for retry", deadLetterId);
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    public Task<bool> Discard(string deadLetterId, CancellationToken cancellationToken = default)
    {
        if (_deadLetters.TryGetValue(deadLetterId, out var entry))
        {
            if (entry is DeadLetterEntry<IMessage> typedEntry)
            {
                typedEntry.Status = DeadLetterStatus.Discarded;
                typedEntry.DiscardedAt = _timeProvider.GetUtcNow().DateTime;

                _logger.LogInformation("Dead letter entry {DeadLetterId} discarded", deadLetterId);
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    public Task<long> GetDeadLetterCount(CancellationToken cancellationToken = default)
    {
        var count = _deadLetters.Values
            .Cast<dynamic>()
            .Count(e => e.Status == DeadLetterStatus.Active);

        return Task.FromResult((long)count);
    }

    public Task<DeadLetterStatistics> GetStatistics(CancellationToken cancellationToken = default)
    {
        var allEntries = _deadLetters.Values.Cast<dynamic>().ToList();

        var stats = new DeadLetterStatistics
        {
            TotalCount = allEntries.Count,
            ActiveCount = allEntries.Count(e => e.Status == DeadLetterStatus.Active),
            RetriedCount = allEntries.Count(e => e.Status == DeadLetterStatus.Retried),
            DiscardedCount = allEntries.Count(e => e.Status == DeadLetterStatus.Discarded)
        };

        // Group by component
        stats.CountByComponent = allEntries
            .GroupBy(e => (string)e.Context.Component)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        // Group by reason (take first 50 chars of reason as key)
        stats.CountByReason = allEntries
            .GroupBy(e => ((string)e.Context.Reason).Length > 50
                ? ((string)e.Context.Reason).Substring(0, 50) + "..."
                : (string)e.Context.Reason)
            .ToDictionary(g => g.Key, g => (long)g.Count());

        if (allEntries.Any())
        {
            stats.OldestEntry = allEntries.Min(e => (DateTime)e.CreatedAt);
            stats.NewestEntry = allEntries.Max(e => (DateTime)e.CreatedAt);
        }

        return Task.FromResult(stats);
    }
}