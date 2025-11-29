using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

public interface IInboxStorage
{
    Task<InboxEntry?> AddAsync(IMessage message, InboxOptions options, CancellationToken cancellationToken = default);

    Task<bool> IsDuplicateAsync(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default);

    Task<InboxEntry?> GetAsync(string messageId, CancellationToken cancellationToken = default);

    Task<bool> MarkProcessedAsync(string messageId, CancellationToken cancellationToken = default);

    Task<bool> MarkFailedAsync(string messageId, string error, CancellationToken cancellationToken = default);

    Task<IEnumerable<InboxEntry>> GetPendingAsync(InboxQuery query, CancellationToken cancellationToken = default);

    Task<IEnumerable<InboxEntry>> GetUnprocessedAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<long> GetUnprocessedCountAsync(CancellationToken cancellationToken = default);

    Task CleanupOldEntriesAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);
}

public class InboxEntry
{
    public required string Id { get; set; }
    public required IMessage Message { get; set; }
    public InboxOptions Options { get; set; } = new();
    public InboxStatus Status { get; set; } = InboxStatus.Pending;
    public DateTimeOffset ReceivedAt { get; set; } = TimeProvider.System.GetUtcNow();
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Status of an inbox entry
/// </summary>
public enum InboxStatus
{
    Pending,
    Processing,
    Processed,
    Failed,
    Duplicate
}

// InboxStatus removed - use InboxStatus instead

public class InboxQuery
{
    public InboxStatus? Status { get; set; }
    public int Limit { get; set; } = 100;
    public DateTimeOffset? OlderThan { get; set; }
    public DateTimeOffset? NewerThan { get; set; }
}
