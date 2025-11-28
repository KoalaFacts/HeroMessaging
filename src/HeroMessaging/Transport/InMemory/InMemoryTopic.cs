using System.Collections.Concurrent;
using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Transport.InMemory;

/// <summary>
/// In-memory topic for pub/sub pattern
/// Broadcasts messages to all subscribed consumers
/// </summary>
internal class InMemoryTopic
{
    private readonly ConcurrentDictionary<string, InMemoryConsumer> _subscriptions = new();
    private readonly ILogger<InMemoryTopic>? _logger;
    private readonly string _topicName;
    private long _publishedCount;
    private long _pendingMessages;

    public InMemoryTopic(string topicName, ILogger<InMemoryTopic>? logger = null)
    {
        _topicName = topicName;
        _logger = logger;
    }

    public long PublishedCount => Interlocked.Read(ref _publishedCount);
    public long PendingMessages => Interlocked.Read(ref _pendingMessages);

    public async Task PublishAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _publishedCount);

        // Broadcast to all subscriptions in parallel
        var tasks = _subscriptions.Values.Select(async subscription =>
        {
            try
            {
                Interlocked.Increment(ref _pendingMessages);
                await subscription.DeliverMessageAsync(envelope, cancellationToken);
            }
            catch (Exception ex)
            {
                // Log error but don't fail publishing
                // Individual consumer failures shouldn't affect other consumers
                _logger?.LogWarning(ex, "Failed to deliver message to consumer {ConsumerId} on topic {TopicName}", subscription.ConsumerId, _topicName);
            }
            finally
            {
                Interlocked.Decrement(ref _pendingMessages);
            }
        });

        await Task.WhenAll(tasks);
    }

    public void AddSubscription(InMemoryConsumer consumer)
    {
        _subscriptions.TryAdd(consumer.ConsumerId, consumer);
    }

    public void RemoveSubscription(InMemoryConsumer consumer)
    {
        _subscriptions.TryRemove(consumer.ConsumerId, out _);
    }
}
