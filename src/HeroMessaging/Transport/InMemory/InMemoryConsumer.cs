using System.Diagnostics;
using System.Threading.Channels;
using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<InMemoryConsumer>? _logger;
    private readonly Channel<TransportEnvelope> _messageChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly CancellationTokenSource _retryDelayCts = new();
    private Task? _processingTask;
    private Task? _stopTask;
    private readonly TimeProvider _timeProvider;
    private TaskCompletionSource<bool>? _drained;
    private int _outstandingDeliveries;
#if NET9_0_OR_GREATER
    private readonly Lock _stateLock = new();
#else
    private readonly object _stateLock = new();
#endif

    private readonly ConsumerMetrics _metrics = new();
#if NET9_0_OR_GREATER
    private readonly Lock _metricsLock = new();
#else
    private readonly object _metricsLock = new();
#endif

    /// <inheritdoc/>
    public string ConsumerId { get; }

    /// <inheritdoc/>
    public TransportAddress Source { get; }

    /// <inheritdoc/>
    public bool IsActive { get; private set; }

    public InMemoryConsumer(
        string consumerId,
        TransportAddress source,
        Func<TransportEnvelope, MessageContext, CancellationToken, Task> handler,
        ConsumerOptions options,
        InMemoryTransport transport,
        TimeProvider timeProvider,
        ITransportInstrumentation? instrumentation = null,
        ILogger<InMemoryConsumer>? logger = null)
    {
        ConsumerId = consumerId ?? throw new ArgumentNullException(nameof(consumerId));
        Source = source;
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _instrumentation = instrumentation ?? NoOpTransportInstrumentation.Instance;
        _logger = logger;

        if (options.ConcurrentMessageLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "ConcurrentMessageLimit must be positive");

        // Bound prefetched messages so the queue can apply backpressure.
        var channelOptions = new BoundedChannelOptions(Math.Max(1, (int)options.PrefetchCount))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        _messageChannel = Channel.CreateBounded<TransportEnvelope>(channelOptions);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            if (IsActive)
                return Task.CompletedTask;

            if (_stopTask is not null || _messageChannel.Reader.Completion.IsCompleted)
                throw new InvalidOperationException("A stopped consumer cannot be restarted");

            IsActive = true;
            _processingTask = Task.WhenAll(Enumerable.Range(0, _options.ConcurrentMessageLimit)
                .Select(_ => ProcessMessagesAsync(_cts.Token)));
        }
        _transport.NotifyConsumerStarted(this);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task stopTask;
        lock (_stateLock)
        {
            if (_stopTask is null && !IsActive)
                return Task.CompletedTask;

            if (_stopTask is null)
            {
                IsActive = false;
                var drained = _drained?.Task ?? Task.CompletedTask;
                _stopTask = Task.Run(() => StopCoreAsync(drained));
            }

            stopTask = _stopTask;
        }

        return stopTask.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public ConsumerMetrics GetMetrics() => _metrics;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
        _retryDelayCts.Dispose();
        _transport.RemoveConsumer(ConsumerId);
    }

    internal async Task DeliverMessageAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (!IsActive)
                throw new ChannelClosedException();

            if (_outstandingDeliveries++ == 0)
                _drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        try
        {
            await _messageChannel.Writer.WriteAsync(envelope, cancellationToken);
        }
        catch
        {
            CompleteDelivery();
            throw;
        }
    }

    private async Task StopCoreAsync(Task drained)
    {
        try
        {
            _transport.NotifyConsumerStopped(this);
            _retryDelayCts.Cancel();
            // A delayed retry still owns its original delivery until its final attempt completes.
            await drained;
            _messageChannel.Writer.TryComplete();

            if (_processingTask is not null)
                await _processingTask;
        }
        finally
        {
            _messageChannel.Writer.TryComplete();
            _cts.Cancel();
        }
    }

    private void CompleteDelivery()
    {
        TaskCompletionSource<bool>? drained = null;
        lock (_stateLock)
        {
            if (--_outstandingDeliveries == 0)
            {
                drained = _drained;
                _drained = null;
            }
        }

        drained?.TrySetResult(true);
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        var reader = _messageChannel.Reader;

        try
        {
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                try
                {
                    while (reader.TryRead(out var envelope))
                    {
                        var retryScheduled = false;
                        try
                        {
                            TransportEnvelope? pending = envelope;
                            while (pending is { } retry)
                                (pending, retryScheduled) = await ProcessMessageAsync(retry, cancellationToken);
                        }
                        finally
                        {
                            if (!retryScheduled)
                                CompleteDelivery();
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error in message processing loop for consumer {ConsumerId}", ConsumerId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<(TransportEnvelope? RequeueEnvelope, bool RetryScheduled)> ProcessMessageAsync(
        TransportEnvelope envelope, CancellationToken cancellationToken)
    {
        var startTime = _timeProvider.GetTimestamp();
        lock (_metricsLock)
        {
            _metrics.CurrentlyProcessing++;
            _metrics.MessagesReceived++;
            _metrics.LastMessageReceived = _timeProvider.GetUtcNow();
        }

        Activity? activity = null;

        // Track whether user manually handled message lifecycle
        bool messageHandled = false;
        TransportEnvelope? requeueEnvelope = null;
        var retryScheduled = false;
        var deferRequested = false;
        var deferDelay = TimeSpan.Zero;

        try
        {
            // Extract trace context from message headers
            var parentContext = _instrumentation.ExtractTraceContext(envelope);

            // Start receive activity with extracted parent context
            activity = _instrumentation.StartReceiveActivity(
                envelope,
                Source.Name,
                _transport.Name,
                ConsumerId,
                parentContext);

            _instrumentation.AddEvent(activity, "receive.start");
            _instrumentation.AddEvent(activity, "handler.start");

            var context = new MessageContext(_transport.Name, Source)
            {
                Acknowledge = async (ct) =>
                {
                    messageHandled = true;
                    lock (_metricsLock)
                        _metrics.MessagesAcknowledged++;
                    _instrumentation.AddEvent(activity, "acknowledge");
                    await Task.CompletedTask;
                },
                Reject = (requeue, ct) =>
                {
                    messageHandled = true;
                    lock (_metricsLock)
                        _metrics.MessagesRejected++;
                    _instrumentation.AddEvent(activity, requeue ? "reject.requeue" : "reject.drop");
                    if (requeue)
                        requeueEnvelope = envelope;

                    return Task.CompletedTask;
                },
                Defer = (delay, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (delay is { } requestedDelay && requestedDelay < TimeSpan.Zero)
                        throw new ArgumentOutOfRangeException(nameof(delay));

                    messageHandled = true;
                    deferRequested = true;
                    deferDelay = delay ?? _options.MessageRetryPolicy.CalculateDelay(envelope.DeliveryCount + 1);
                    _instrumentation.AddEvent(activity, "defer");
                    return Task.CompletedTask;
                },
                DeadLetter = async (reason, ct) =>
                {
                    messageHandled = true;
                    lock (_metricsLock)
                        _metrics.MessagesDeadLettered++;
                    _instrumentation.AddEvent(activity, "deadletter",
                    [
                        new KeyValuePair<string, object?>("reason", reason ?? "unknown")
                    ]);
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

            var duration = _timeProvider.GetElapsedTime(startTime);
            lock (_metricsLock)
            {
                _metrics.RecordSuccess();
                _metrics.LastMessageProcessed = _timeProvider.GetUtcNow();
                UpdateAverageProcessingDuration(duration);
            }

            var durationMs = duration.TotalMilliseconds;
            _instrumentation.RecordReceiveDuration(_transport.Name, Source.Name, envelope.MessageType, durationMs);
            _instrumentation.RecordOperation(_transport.Name, "receive", "success");
        }
        catch (Exception ex)
        {
            lock (_metricsLock)
                _metrics.RecordFailure(ex.Message, _timeProvider);

            // Record error
            _instrumentation.RecordError(activity, ex);
            _instrumentation.RecordOperation(_transport.Name, "receive", "failure");

            // Retry logic - schedule retry asynchronously to avoid blocking other messages
            var retryCount = envelope.DeliveryCount;
            if (!messageHandled && retryCount < _options.MessageRetryPolicy.MaxAttempts)
            {
                var delay = _options.MessageRetryPolicy.CalculateDelay(retryCount + 1);
                var retryEnvelope = envelope with { DeliveryCount = retryCount + 1 };

                _ = ScheduleRetryAsync(retryEnvelope, delay);
                retryScheduled = true;
            }
            else if (!messageHandled)
            {
                // Dead letter after max retries
                lock (_metricsLock)
                    _metrics.MessagesDeadLettered++;
            }
        }
        finally
        {
            if (deferRequested)
            {
                _ = ScheduleRetryAsync(envelope with { DeliveryCount = envelope.DeliveryCount + 1 }, deferDelay);
                retryScheduled = true;
            }

            activity?.Dispose();
            lock (_metricsLock)
                _metrics.CurrentlyProcessing--;
        }

        return (requeueEnvelope, retryScheduled);
    }

    private async Task ScheduleRetryAsync(TransportEnvelope envelope, TimeSpan delay)
    {
        var enqueued = false;
        try
        {
            try
            {
                await Task.Delay(delay, _timeProvider, _retryDelayCts.Token);
            }
            catch (OperationCanceledException) when (_retryDelayCts.IsCancellationRequested)
            {
                // A graceful stop skips the delay, not the retry.
            }

            await _messageChannel.Writer.WriteAsync(envelope, _cts.Token);
            enqueued = true;
        }
        catch (ChannelClosedException ex)
        {
            _logger?.LogWarning(ex, "Failed to schedule retry for consumer {ConsumerId}", ConsumerId);
        }
        catch (OperationCanceledException ex) when (_cts.IsCancellationRequested)
        {
            _logger?.LogWarning(ex, "Failed to schedule retry for consumer {ConsumerId}", ConsumerId);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger?.LogWarning(ex, "Failed to schedule retry for consumer {ConsumerId}", ConsumerId);
        }
        finally
        {
            if (!enqueued)
            {
                lock (_metricsLock)
                    _metrics.MessagesDeadLettered++;
                CompleteDelivery();
            }
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
