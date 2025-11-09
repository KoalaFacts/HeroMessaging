using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.ErrorHandling;

public interface IDeadLetterQueue
{
    Task<string> SendToDeadLetterAsync<T>(T message, DeadLetterContext context, CancellationToken cancellationToken = default) where T : IMessage;

    Task<IEnumerable<DeadLetterEntry<T>>> GetDeadLettersAsync<T>(int limit = 100, CancellationToken cancellationToken = default) where T : IMessage;

    Task<bool> RetryAsync<T>(string deadLetterId, CancellationToken cancellationToken = default) where T : IMessage;

    Task<bool> DiscardAsync(string deadLetterId, CancellationToken cancellationToken = default);

    Task<long> GetDeadLetterCountAsync(CancellationToken cancellationToken = default);

    Task<DeadLetterStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

public sealed record DeadLetterContext
{
    public string Reason { get; init; } = string.Empty;
    public Exception? Exception { get; init; }
    public string Component { get; init; } = string.Empty;
    public int RetryCount { get; init; }
    public DateTime FailureTime { get; init; } = TimeProvider.System.GetUtcNow().DateTime;
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Non-generic base interface for dead letter entries
/// </summary>
public interface IDeadLetterEntry
{
    string Id { get; }
    DeadLetterContext Context { get; }
    DateTime CreatedAt { get; }
    DeadLetterStatus Status { get; }
    DateTime? RetriedAt { get; }
    DateTime? DiscardedAt { get; }
}

public sealed record DeadLetterEntry<T> : IDeadLetterEntry where T : IMessage
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public T Message { get; init; } = default!;
    public DeadLetterContext Context { get; init; } = new();
    public DateTime CreatedAt { get; init; } = TimeProvider.System.GetUtcNow().DateTime;
    public DeadLetterStatus Status { get; init; } = DeadLetterStatus.Active;
    public DateTime? RetriedAt { get; init; }
    public DateTime? DiscardedAt { get; init; }
}

public enum DeadLetterStatus
{
    Active,
    Retried,
    Discarded,
    Expired
}

public sealed record DeadLetterStatistics
{
    public long TotalCount { get; init; }
    public long ActiveCount { get; init; }
    public long RetriedCount { get; init; }
    public long DiscardedCount { get; init; }
    public Dictionary<string, long> CountByComponent { get; init; } = new();
    public Dictionary<string, long> CountByReason { get; init; } = new();
    public DateTime? OldestEntry { get; init; }
    public DateTime? NewestEntry { get; init; }
}