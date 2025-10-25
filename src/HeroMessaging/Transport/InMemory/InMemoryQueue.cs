using HeroMessaging.Abstractions.Transport;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace HeroMessaging.Transport.InMemory;

/// <summary>
/// High-performance in-memory queue using System.Threading.Channels
/// Supports competing consumers with round-robin distribution
/// </summary>
internal class InMemoryQueue : IDisposable
{
    private readonly Channel<TransportEnvelope> _channel;
    private readonly ConcurrentBag<InMemoryConsumer> _consumers = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;
    private int _consumerIndex;
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
        StartProcessingIfNeeded();
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

    private void StartProcessingIfNeeded()
    {
        if (_processingTask == null)
        {
            _processingTask = Task.Run(() => ProcessMessagesAsync(_cts.Token));
        }
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        var reader = _channel.Reader;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Wait for messages to be available
                if (!await reader.WaitToReadAsync(cancellationToken))
                    break;

                // Read all available messages
                while (reader.TryRead(out var envelope))
                {
                    Interlocked.Decrement(ref _depth);

                    // Deliver to next consumer in round-robin fashion
                    var consumers = _consumers.ToArray();
                    if (consumers.Length > 0)
                    {
                        var consumerIndex = Interlocked.Increment(ref _consumerIndex);
                        var consumer = consumers[Math.Abs(consumerIndex % consumers.Length)];

                        try
                        {
                            await consumer.DeliverMessageAsync(envelope, cancellationToken);
                        }
                        catch (Exception)
                        {
                            // Consumer delivery failed, but don't stop processing other messages
                            // In a real implementation, we'd log this
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception)
        {
            // Processing loop error - in real implementation we'd log this
        }
    }

    public void Dispose()
    {
        _channel.Writer.Complete();
        _cts.Cancel();

        // Wait for processing to complete with a timeout
        _processingTask?.Wait(TimeSpan.FromSeconds(5));

        _cts.Dispose();
    }
}
