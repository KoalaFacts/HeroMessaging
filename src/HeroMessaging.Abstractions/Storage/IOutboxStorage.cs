using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

public interface IOutboxStorage
{
    Task<OutboxEntry> AddAsync(IMessage message, Abstractions.OutboxOptions options, CancellationToken cancellationToken = default);

    Task<IEnumerable<OutboxEntry>> GetPendingAsync(OutboxQuery query, CancellationToken cancellationToken = default);

    Task<IEnumerable<OutboxEntry>> GetPendingAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<bool> MarkProcessedAsync(string entryId, CancellationToken cancellationToken = default);

    Task<bool> MarkFailedAsync(string entryId, string error, CancellationToken cancellationToken = default);

    Task<bool> UpdateRetryCountAsync(string entryId, int retryCount, DateTimeOffset? nextRetry = null, CancellationToken cancellationToken = default);

    Task<long> GetPendingCountAsync(CancellationToken cancellationToken = default);

    Task<IEnumerable<OutboxEntry>> GetFailedAsync(int limit = 100, CancellationToken cancellationToken = default);
}

public class OutboxEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public IMessage Message { get; set; } = null!;
    public Abstractions.OutboxOptions Options { get; set; } = new();
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int RetryCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = TimeProvider.System.GetUtcNow();
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
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
    public DateTimeOffset? OlderThan { get; set; }
    public DateTimeOffset? NewerThan { get; set; }
}