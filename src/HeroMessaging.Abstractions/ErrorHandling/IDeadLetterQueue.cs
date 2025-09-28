using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.ErrorHandling;

public interface IDeadLetterQueue
{
    Task<string> SendToDeadLetter<T>(T message, DeadLetterContext context, CancellationToken cancellationToken = default) where T : IMessage;

    Task<IEnumerable<DeadLetterEntry<T>>> GetDeadLetters<T>(int limit = 100, CancellationToken cancellationToken = default) where T : IMessage;

    Task<bool> Retry<T>(string deadLetterId, CancellationToken cancellationToken = default) where T : IMessage;

    Task<bool> Discard(string deadLetterId, CancellationToken cancellationToken = default);

    Task<long> GetDeadLetterCount(CancellationToken cancellationToken = default);

    Task<DeadLetterStatistics> GetStatistics(CancellationToken cancellationToken = default);
}

public class DeadLetterContext
{
    public string Reason { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
    public string Component { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public DateTime FailureTime { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class DeadLetterEntry<T> where T : IMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public T Message { get; set; } = default!;
    public DeadLetterContext Context { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DeadLetterStatus Status { get; set; } = DeadLetterStatus.Active;
    public DateTime? RetriedAt { get; set; }
    public DateTime? DiscardedAt { get; set; }
}

public enum DeadLetterStatus
{
    Active,
    Retried,
    Discarded,
    Expired
}

public class DeadLetterStatistics
{
    public long TotalCount { get; set; }
    public long ActiveCount { get; set; }
    public long RetriedCount { get; set; }
    public long DiscardedCount { get; set; }
    public Dictionary<string, long> CountByComponent { get; set; } = new();
    public Dictionary<string, long> CountByReason { get; set; } = new();
    public DateTime? OldestEntry { get; set; }
    public DateTime? NewestEntry { get; set; }
}