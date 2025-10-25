using HeroMessaging.Abstractions.Transport;
using System.Threading.Channels;

namespace HeroMessaging.Transport.InMemory;

/// <summary>
/// In-memory consumer implementation
/// Processes messages from queues or topic subscriptions
/// </summary>
internal class InMemoryConsumer : ITransportConsumer
{
    private readonly Func<TransportEnvelope, MessageContext, CancellationToken, Task> _handler;
    private readonly ConsumerOptions _options;
    private readonly InMemoryTransport _transport;
    private readonly Channel<TransportEnvelope> _messageChannel;
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;
    private bool _isActive;

    private readonly ConsumerMetrics _metrics = new();
    private readonly SemaphoreSlim _concurrencyLimiter;

    /// <inheritdoc/>
    public string ConsumerId { get; }

    /// <inheritdoc/>
    public TransportAddress Source { get; }

    /// <inheritdoc/>
    public bool IsActive => _isActive;

    public InMemoryConsumer(
        string consumerId,
        TransportAddress source,
        Func<TransportEnvelope, MessageContext, CancellationToken, Task> handler,
        ConsumerOptions options,
        InMemoryTransport transport)
    {
        ConsumerId = consumerId ?? throw new ArgumentNullException(nameof(consumerId));
        Source = source;
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));

        _concurrencyLimiter = new SemaphoreSlim(options.ConcurrentMessageLimit, options.ConcurrentMessageLimit);

        // Create internal channel for message delivery
        var channelOptions = new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        };
        _messageChannel = Channel.CreateUnbounded<TransportEnvelope>(channelOptions);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isActive)
            return Task.CompletedTask;

        _isActive = true;
        _processingTask = ProcessMessagesAsync(_cts.Token);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isActive)
            return;

        _isActive = false;
        _messageChannel.Writer.Complete();
        _cts.Cancel();

        if (_processingTask != null)
        {
            await _processingTask;
        }
    }

    /// <inheritdoc/>
    public ConsumerMetrics GetMetrics() => _metrics;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
        _concurrencyLimiter.Dispose();
        _transport.RemoveConsumer(ConsumerId);
    }

    internal async Task DeliverMessageAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (!_isActive)
            return;

        await _messageChannel.Writer.WriteAsync(envelope, cancellationToken);
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        var reader = _messageChannel.Reader;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Wait for messages
                if (!await reader.WaitToReadAsync(cancellationToken))
                    break;

                // Process messages with concurrency limit
                while (reader.TryRead(out var envelope))
                {
                    _ = ProcessMessageAsync(envelope, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Continue processing on error
            }
        }
    }

    private async Task ProcessMessageAsync(TransportEnvelope envelope, CancellationToken cancellationToken)
    {
        await _concurrencyLimiter.WaitAsync(cancellationToken);
        _metrics.CurrentlyProcessing++;

        var startTime = DateTime.UtcNow;
        _metrics.MessagesReceived++;
        _metrics.LastMessageReceived = startTime;

        try
        {
            var context = new MessageContext(_transport.Name, Source)
            {
                Acknowledge = async (ct) =>
                {
                    _metrics.MessagesAcknowledged++;
                    await Task.CompletedTask;
                },
                Reject = async (requeue, ct) =>
                {
                    _metrics.MessagesRejected++;
                    if (requeue)
                    {
                        await DeliverMessageAsync(envelope, ct);
                    }
                },
                DeadLetter = async (reason, ct) =>
                {
                    _metrics.MessagesDeadLettered++;
                    await Task.CompletedTask;
                }
            };

            // Invoke user handler
            await _handler(envelope, context, cancellationToken);

            // Auto-acknowledge if configured
            if (_options.AutoAcknowledge)
            {
                await context.AcknowledgeAsync(cancellationToken);
            }

            _metrics.RecordSuccess();
            _metrics.LastMessageProcessed = DateTime.UtcNow;

            // Update average processing duration
            var duration = DateTime.UtcNow - startTime;
            UpdateAverageProcessingDuration(duration);
        }
        catch (Exception ex)
        {
            _metrics.RecordFailure(ex.Message);

            // Retry logic
            var retryCount = envelope.DeliveryCount;
            if (retryCount < _options.MessageRetryPolicy.MaxAttempts)
            {
                var delay = _options.MessageRetryPolicy.CalculateDelay(retryCount + 1);
                await Task.Delay(delay, cancellationToken);

                var retryEnvelope = envelope with { DeliveryCount = retryCount + 1 };
                await DeliverMessageAsync(retryEnvelope, cancellationToken);
            }
            else
            {
                // Dead letter after max retries
                _metrics.MessagesDeadLettered++;
            }
        }
        finally
        {
            _metrics.CurrentlyProcessing--;
            _concurrencyLimiter.Release();
        }
    }

    private void UpdateAverageProcessingDuration(TimeSpan duration)
    {
        // Simple moving average
        var total = _metrics.MessagesProcessed;
        if (total == 0)
        {
            _metrics.AverageProcessingDuration = duration;
        }
        else
        {
            var currentAvg = _metrics.AverageProcessingDuration.TotalMilliseconds;
            var newAvg = ((currentAvg * (total - 1)) + duration.TotalMilliseconds) / total;
            _metrics.AverageProcessingDuration = TimeSpan.FromMilliseconds(newAvg);
        }
    }
}
