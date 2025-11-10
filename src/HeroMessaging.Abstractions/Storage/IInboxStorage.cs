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
    public string Id { get; set; } = null!;
    public IMessage Message { get; set; } = null!;
    public InboxOptions Options { get; set; } = new();
    public InboxStatus Status { get; set; } = InboxStatus.Pending;
    public DateTime ReceivedAt { get; set; } = TimeProvider.System.GetUtcNow().DateTime;
    public DateTime? ProcessedAt { get; set; }
    public string? Error { get; set; }
}

public enum InboxStatus
{
    Pending,
    Processing,
    Processed,
    Failed,
    Duplicate
}

public enum InboxEntryStatus
{
    Pending,
    Processing,
    Processed,
    Failed,
    Duplicate
}

public class InboxQuery
{
    public InboxEntryStatus? Status { get; set; }
    public int Limit { get; set; } = 100;
    public DateTime? OlderThan { get; set; }
    public DateTime? NewerThan { get; set; }
}