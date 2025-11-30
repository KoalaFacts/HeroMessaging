using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

public interface IQueueStorage
{
    Task<QueueEntry> EnqueueAsync(string queueName, IMessage message, EnqueueOptions? options = null, CancellationToken cancellationToken = default);

    Task<QueueEntry?> DequeueAsync(string queueName, CancellationToken cancellationToken = default);

    Task<IEnumerable<QueueEntry>> PeekAsync(string queueName, int count = 1, CancellationToken cancellationToken = default);

    Task<bool> AcknowledgeAsync(string queueName, string entryId, CancellationToken cancellationToken = default);

    Task<bool> RejectAsync(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default);

    Task<long> GetQueueDepthAsync(string queueName, CancellationToken cancellationToken = default);

    Task<bool> CreateQueueAsync(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default);

    Task<bool> DeleteQueueAsync(string queueName, CancellationToken cancellationToken = default);

    Task<IEnumerable<string>> GetQueuesAsync(CancellationToken cancellationToken = default);

    Task<bool> QueueExistsAsync(string queueName, CancellationToken cancellationToken = default);
}

public class QueueEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public IMessage Message { get; set; } = null!;
    public EnqueueOptions Options { get; set; } = new();
    public DateTimeOffset EnqueuedAt { get; set; } = TimeProvider.System.GetUtcNow();
    public DateTimeOffset? VisibleAt { get; set; }
    public int DequeueCount { get; set; }
}

public class QueueOptions
{
    public int? MaxSize { get; set; }
    public TimeSpan? MessageTtl { get; set; }
    public int? MaxDequeueCount { get; set; }
    public TimeSpan? VisibilityTimeout { get; set; }
    public bool EnablePriority { get; set; } = true;
}
