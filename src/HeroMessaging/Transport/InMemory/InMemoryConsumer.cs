using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;
using System.Diagnostics;
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
    private readonly ITransportInstrumentation _instrumentation;
    private readonly Channel<TransportEnvelope> _messageChannel;
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;
    private bool _isActive;
    private readonly TimeProvider _timeProvider;

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
        InMemoryTransport transport,
        TimeProvider timeProvider,
        ITransportInstrumentation? instrumentation = null)
    {
        ConsumerId = consumerId ?? throw new ArgumentNullException(nameof(consumerId));
        Source = source;
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _instrumentation = instrumentation ?? NoOpTransportInstrumentation.Instance;

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

        var startTime = Stopwatch.GetTimestamp();
        _metrics.MessagesReceived++;
        _metrics.LastMessageReceived = _timeProvider.GetUtcNow().DateTime;

        Activity? activity = null;

        // Track whether user manually handled message lifecycle
        bool messageHandled = false;

        try
        {
            _instrumentation.AddEvent(activity, "receive.start");

            // Extract trace context from message headers
            var parentContext = _instrumentation.ExtractTraceContext(envelope);

            // Start receive activity with extracted parent context
            activity = _instrumentation.StartReceiveActivity(
                envelope,
                Source.Name,
                _transport.Name,
                ConsumerId,
                parentContext);

            _instrumentation.AddEvent(activity, "handler.start");

            var context = new MessageContext(_transport.Name, Source)
            {
                Acknowledge = async (ct) =>
                {
                    messageHandled = true;
                    _metrics.MessagesAcknowledged++;
                    _instrumentation.AddEvent(activity, "acknowledge");
                    await Task.CompletedTask;
                },
                Reject = async (requeue, ct) =>
                {
                    messageHandled = true;
                    _metrics.MessagesRejected++;
                    _instrumentation.AddEvent(activity, requeue ? "reject.requeue" : "reject.drop");
                    if (requeue)
                    {
                        await DeliverMessageAsync(envelope, ct);
                    }
                },
                Defer = async (delay, ct) =>
                {
                    messageHandled = true;
                    _instrumentation.AddEvent(activity, "defer");
                    await Task.CompletedTask;
                },
                DeadLetter = async (reason, ct) =>
                {
                    messageHandled = true;
                    _metrics.MessagesDeadLettered++;
                    _instrumentation.AddEvent(activity, "deadletter", new[]
                    {
                        new KeyValuePair<string, object?>("reason", reason ?? "unknown")
                    });
                    await Task.CompletedTask;
                }
            };

            // Invoke user handler
            await _handler(envelope, context, cancellationToken);

            _instrumentation.AddEvent(activity, "handler.complete");

            // Auto-acknowledge if configured and user didn't manually handle the message
            if (_options.AutoAcknowledge && !messageHandled)
            {
                await context.AcknowledgeAsync(cancellationToken);
            }

            _metrics.RecordSuccess();
            _metrics.LastMessageProcessed = _timeProvider.GetUtcNow().DateTime;

            // Record successful operation
            var durationMs = GetElapsedMilliseconds(startTime);
            _instrumentation.RecordReceiveDuration(_transport.Name, Source.Name, envelope.MessageType, durationMs);
            _instrumentation.RecordOperation(_transport.Name, "receive", "success");

            // Update average processing duration
            var duration = _timeProvider.GetUtcNow().DateTime - _metrics.LastMessageReceived;
            UpdateAverageProcessingDuration(duration ?? TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            _metrics.RecordFailure(ex.Message);

            // Record error
            _instrumentation.RecordError(activity, ex);
            _instrumentation.RecordOperation(_transport.Name, "receive", "failure");

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
            activity?.Dispose();
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

    /// <summary>
    /// Calculate elapsed milliseconds from timestamp (compatible with netstandard2.0)
    /// </summary>
    private static double GetElapsedMilliseconds(long startTimestamp)
    {
        var elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
        return (elapsedTicks * 1000.0) / Stopwatch.Frequency;
    }
}
