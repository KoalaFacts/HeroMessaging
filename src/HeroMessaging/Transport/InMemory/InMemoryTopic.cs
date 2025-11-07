using HeroMessaging.Abstractions.Transport;
using System.Collections.Concurrent;

namespace HeroMessaging.Transport.InMemory;

/// <summary>
/// In-memory topic for pub/sub pattern
/// Broadcasts messages to all subscribed consumers
/// </summary>
internal class InMemoryTopic
{
    private readonly ConcurrentDictionary<string, InMemoryConsumer> _subscriptions = new();
    private long _publishedCount;
    private long _pendingMessages;

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
            catch (Exception)
            {
                // Log error but don't fail publishing
                // Individual consumer failures shouldn't affect other consumers
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
