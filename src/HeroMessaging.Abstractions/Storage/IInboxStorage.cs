using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

public interface IInboxStorage
{
    Task<InboxEntry?> Add(IMessage message, InboxOptions options, CancellationToken cancellationToken = default);
    
    Task<bool> IsDuplicate(string messageId, TimeSpan? window = null, CancellationToken cancellationToken = default);
    
    Task<InboxEntry?> Get(string messageId, CancellationToken cancellationToken = default);
    
    Task<bool> MarkProcessed(string messageId, CancellationToken cancellationToken = default);
    
    Task<bool> MarkFailed(string messageId, string error, CancellationToken cancellationToken = default);
    
    Task<IEnumerable<InboxEntry>> GetPending(InboxQuery query, CancellationToken cancellationToken = default);
    
    Task<IEnumerable<InboxEntry>> GetUnprocessed(int limit = 100, CancellationToken cancellationToken = default);
    
    Task<long> GetUnprocessedCount(CancellationToken cancellationToken = default);
    
    Task CleanupOldEntries(TimeSpan olderThan, CancellationToken cancellationToken = default);
}

public class InboxEntry
{
    public string Id { get; set; } = null!;
    public IMessage Message { get; set; } = null!;
    public InboxOptions Options { get; set; } = new();
    public InboxStatus Status { get; set; } = InboxStatus.Pending;
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
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