using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Transport.InMemory;

/// <summary>
/// High-performance in-memory queue using System.Threading.Channels
/// Supports competing consumers with round-robin distribution
/// </summary>
internal class InMemoryQueue : IDisposable, IAsyncDisposable
{
    private readonly Channel<TransportEnvelope> _channel;
    private readonly ConcurrentDictionary<string, InMemoryConsumer> _consumers = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<InMemoryQueue>? _logger;
    private readonly string _queueName;
    private Task? _processingTask;
#if NET9_0_OR_GREATER
    private readonly Lock _processingTaskLock = new();
#else
    private readonly object _processingTaskLock = new();
#endif
    private int _consumerIndex;
    private long _messageCount;
    private long _depth;
    private readonly int _maxQueueLength;
    private readonly bool _dropWhenFull;

    // Performance optimization: Cache consumer array to avoid allocation on every message
    private InMemoryConsumer[] _consumerCache = [];
    private volatile int _consumerVersion; // Increment when consumers change

    public long MessageCount => Interlocked.Read(ref _messageCount);
    public long Depth => Interlocked.Read(ref _depth);

    public InMemoryQueue(string queueName, int maxQueueLength, bool dropWhenFull, ILogger<InMemoryQueue>? logger = null)
    {
        _queueName = queueName;
        _maxQueueLength = maxQueueLength;
        _dropWhenFull = dropWhenFull;
        _logger = logger;

        // IMPORTANT: When dropWhenFull=false, the channel uses BoundedChannelFullMode.Wait
        // This means EnqueueAsync will WAIT INDEFINITELY for space when the queue is full
        // Callers should use CancellationToken timeout to prevent indefinite blocking
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

            // In DropOldest mode, depth should never exceed maxQueueLength
            // because the channel drops the oldest message when full
            if (_dropWhenFull)
            {
                // Use a compare-exchange loop to ensure depth doesn't exceed max
                long currentDepth;
                long newDepth;
                do
                {
                    currentDepth = Interlocked.Read(ref _depth);
                    newDepth = Math.Min(currentDepth + 1, _maxQueueLength);
                } while (Interlocked.CompareExchange(ref _depth, newDepth, currentDepth) != currentDepth);
            }
            else
            {
                Interlocked.Increment(ref _depth);
            }

            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    public void AddConsumer(InMemoryConsumer consumer)
    {
        _consumers.TryAdd(consumer.ConsumerId, consumer);
        Interlocked.Increment(ref _consumerVersion); // Invalidate cache
        StartProcessingIfNeeded();
    }

    public void RemoveConsumer(InMemoryConsumer consumer)
    {
        _consumers.TryRemove(consumer.ConsumerId, out _);
        Interlocked.Increment(ref _consumerVersion); // Invalidate cache
    }

    private void StartProcessingIfNeeded()
    {
        lock (_processingTaskLock)
        {
            _processingTask ??= Task.Run(() => ProcessMessagesAsync(_cts.Token));
        }
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        var reader = _channel.Reader;
        var cachedConsumers = _consumerCache;
        var cachedVersion = _consumerVersion;

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

                    // Check if consumer list changed - refresh cache if needed
                    var currentVersion = _consumerVersion;
                    if (currentVersion != cachedVersion || cachedConsumers.Length == 0)
                    {
                        cachedConsumers = [.. _consumers.Values];
                        _consumerCache = cachedConsumers; // Update shared cache
                        cachedVersion = currentVersion;
                    }

                    // Deliver to next consumer in round-robin fashion
                    if (cachedConsumers.Length > 0)
                    {
                        // Use unchecked to allow natural overflow wrap-around
                        // This is faster than Math.Abs and handles negative values correctly
                        var index = unchecked((uint)Interlocked.Increment(ref _consumerIndex));
                        var consumer = cachedConsumers[index % (uint)cachedConsumers.Length];

                        try
                        {
                            await consumer.DeliverMessageAsync(envelope, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            // Consumer delivery failed, but don't stop processing other messages
                            _logger?.LogWarning(ex, "Failed to deliver message to consumer {ConsumerId} on queue {QueueName}", consumer.ConsumerId, _queueName);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in processing loop for queue {QueueName}", _queueName);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.Complete();
        _cts.Cancel();

        // Wait for processing to complete asynchronously
        if (_processingTask != null)
        {
            try
            {
                // Properly await the task completion
                await _processingTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore exceptions during disposal
            }
        }

        _cts.Dispose();
    }

    public void Dispose()
    {
        // Synchronous disposal calls async version
        // This is acceptable for cleanup scenarios
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
