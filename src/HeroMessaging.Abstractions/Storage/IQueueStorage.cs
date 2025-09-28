using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Storage;

public interface IQueueStorage
{
    Task<QueueEntry> Enqueue(string queueName, IMessage message, EnqueueOptions? options = null, CancellationToken cancellationToken = default);

    Task<QueueEntry?> Dequeue(string queueName, CancellationToken cancellationToken = default);

    Task<IEnumerable<QueueEntry>> Peek(string queueName, int count = 1, CancellationToken cancellationToken = default);

    Task<bool> Acknowledge(string queueName, string entryId, CancellationToken cancellationToken = default);

    Task<bool> Reject(string queueName, string entryId, bool requeue = false, CancellationToken cancellationToken = default);

    Task<long> GetQueueDepth(string queueName, CancellationToken cancellationToken = default);

    Task<bool> CreateQueue(string queueName, QueueOptions? options = null, CancellationToken cancellationToken = default);

    Task<bool> DeleteQueue(string queueName, CancellationToken cancellationToken = default);

    Task<IEnumerable<string>> GetQueues(CancellationToken cancellationToken = default);

    Task<bool> QueueExists(string queueName, CancellationToken cancellationToken = default);
}

public class QueueEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public IMessage Message { get; set; } = null!;
    public EnqueueOptions Options { get; set; } = new();
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? VisibleAt { get; set; }
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