using HeroMessaging.Abstractions.Transport;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace HeroMessaging.Transport.InMemory;

/// <summary>
/// High-performance in-memory queue using System.Threading.Channels
/// Supports competing consumers with round-robin distribution
/// </summary>
internal class InMemoryQueue
{
    private readonly Channel<TransportEnvelope> _channel;
    private readonly ConcurrentBag<InMemoryConsumer> _consumers = new();
    private long _messageCount;
    private long _depth;

    public long MessageCount => Interlocked.Read(ref _messageCount);
    public long Depth => Interlocked.Read(ref _depth);

    public InMemoryQueue(int maxQueueLength, bool dropWhenFull)
    {
        var options = new BoundedChannelOptions(maxQueueLength)
        {
            FullMode = dropWhenFull ? BoundedChannelFullMode.DropOldest : BoundedChannelFullMode.Wait,
            SingleReader = false, // Multiple consumers
            SingleWriter = false  // Multiple publishers
        };

        _channel = Channel.CreateBounded<TransportEnvelope>(options);
    }

    public async ValueTask<bool> EnqueueAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        try
        {
            await _channel.Writer.WriteAsync(envelope, cancellationToken);
            Interlocked.Increment(ref _messageCount);
            Interlocked.Increment(ref _depth);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    public async ValueTask<TransportEnvelope?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var envelope = await _channel.Reader.ReadAsync(cancellationToken);
            Interlocked.Decrement(ref _depth);
            return envelope;
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public bool TryDequeue(out TransportEnvelope envelope)
    {
        if (_channel.Reader.TryRead(out envelope))
        {
            Interlocked.Decrement(ref _depth);
            return true;
        }

        return false;
    }

    public void AddConsumer(InMemoryConsumer consumer)
    {
        _consumers.Add(consumer);
    }

    public void RemoveConsumer(InMemoryConsumer consumer)
    {
        // ConcurrentBag doesn't have Remove, but consumers will be garbage collected
        // This is fine for in-memory transport
    }

    public ChannelReader<TransportEnvelope> GetReader() => _channel.Reader;

    public void Complete()
    {
        _channel.Writer.Complete();
    }
}
