using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

public interface IOutboxStorage
{
    Task<OutboxEntry> Add(IMessage message, Abstractions.OutboxOptions options, CancellationToken cancellationToken = default);

    Task<IEnumerable<OutboxEntry>> GetPending(OutboxQuery query, CancellationToken cancellationToken = default);

    Task<IEnumerable<OutboxEntry>> GetPending(int limit = 100, CancellationToken cancellationToken = default);

    Task<bool> MarkProcessed(string entryId, CancellationToken cancellationToken = default);

    Task<bool> MarkFailed(string entryId, string error, CancellationToken cancellationToken = default);

    Task<bool> UpdateRetryCount(string entryId, int retryCount, DateTime? nextRetry = null, CancellationToken cancellationToken = default);

    Task<long> GetPendingCount(CancellationToken cancellationToken = default);

    Task<IEnumerable<OutboxEntry>> GetFailed(int limit = 100, CancellationToken cancellationToken = default);
}

public class OutboxEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public IMessage Message { get; set; } = null!;
    public Abstractions.OutboxOptions Options { get; set; } = new();
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public string? LastError { get; set; }
}

public enum OutboxStatus
{
    Pending,
    Processing,
    Processed,
    Failed
}

public enum OutboxEntryStatus
{
    Pending,
    Processing,
    Processed,
    Failed
}

public class OutboxQuery
{
    public OutboxEntryStatus? Status { get; set; }
    public int Limit { get; set; } = 100;
    public DateTime? OlderThan { get; set; }
    public DateTime? NewerThan { get; set; }
}