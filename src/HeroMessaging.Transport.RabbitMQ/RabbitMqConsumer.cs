using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;

namespace HeroMessaging.Transport.RabbitMQ;

internal interface IRabbitMqConsumerHost
{
    string Name { get; }

    void RemoveConsumer(string consumerId);
}

/// <summary>
/// RabbitMQ implementation of ITransportConsumer
/// </summary>
internal sealed class RabbitMqConsumer : ITransportConsumer
{
    private readonly IChannel _channel;
    private readonly Func<TransportEnvelope, MessageContext, CancellationToken, Task> _handler;
    private readonly ConsumerOptions _options;
    private readonly IRabbitMqConsumerHost _transport;
    private readonly ILogger<RabbitMqConsumer> _logger;
    private readonly ITransportInstrumentation _instrumentation;
    private readonly TimeProvider _timeProvider;
    private AsyncEventingBasicConsumer? _consumer;
    private string? _consumerTag;
    private long _messagesProcessed;
    private long _messagesFailed;
    private int _inFlightDeliveries;
    private TaskCompletionSource? _deliveriesDrained;
    private TaskCompletionSource? _consumerUnregistered;
    private TaskCompletionSource? _startCompleted;
    private Task? _stopTask;
    private Task? _disposeTask;
    private readonly AsyncLocal<DeliveryScope?> _currentDelivery = new();
#if NET9_0_OR_GREATER
    private readonly Lock _stateLock = new();
#else
    private readonly object _stateLock = new();
#endif

    /// <inheritdoc/>
    public string ConsumerId { get; }

    /// <inheritdoc/>
    public TransportAddress Source { get; }

    /// <inheritdoc/>
    public bool IsActive { get; private set; }

    public RabbitMqConsumer(
        string consumerId,
        TransportAddress source,
        IChannel channel,
        Func<TransportEnvelope, MessageContext, CancellationToken, Task> handler,
        ConsumerOptions options,
        IRabbitMqConsumerHost transport,
        ILogger<RabbitMqConsumer> logger,
        TimeProvider timeProvider,
        ITransportInstrumentation? instrumentation = null)
    {
        ConsumerId = string.IsNullOrEmpty(consumerId)
            ? throw new ArgumentNullException(nameof(consumerId))
            : consumerId;
        if (string.IsNullOrEmpty(source.Name))
            throw new ArgumentException("Source address name cannot be null or empty", nameof(source));
        Source = source;
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _instrumentation = instrumentation ?? NoOpTransportInstrumentation.Instance;
    }

    /// <summary>
    /// Start consuming messages
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (IsActive)
            {
                _logger.LogWarning("Consumer {ConsumerId} is already active", ConsumerId);
                return;
            }
            if (_stopTask is { IsCompleted: false })
                throw new InvalidOperationException($"Consumer {ConsumerId} is still stopping");

            _logger.LogInformation("Starting consumer {ConsumerId} for {Source}", ConsumerId, Source.Name);
            _stopTask = null;
            _consumerUnregistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _startCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            IsActive = true;
        }

        try
        {
            _consumer = new AsyncEventingBasicConsumer(_channel);
            _consumer.ReceivedAsync += OnMessageReceivedAsync;
            _consumer.RegisteredAsync += OnConsumerRegisteredAsync;
            _consumer.UnregisteredAsync += OnConsumerUnregisteredAsync;
            _consumer.ShutdownAsync += OnConsumerShutdownAsync;

            await _channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: _options.PrefetchCount,
                global: false,
                cancellationToken).ConfigureAwait(false);

            _consumerTag = await _channel.BasicConsumeAsync(
                queue: Source.Name,
                autoAck: false, // Manual acknowledgment for reliability
                consumer: _consumer,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Consumer {ConsumerId} started with tag {ConsumerTag}", ConsumerId, _consumerTag);
        }
        finally
        {
            _startCompleted.TrySetResult();
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        var stopTask = BeginStop();
        // The current callback cannot drain until its own StopAsync call returns.
        if (IsInCurrentDelivery)
            return Task.CompletedTask;
        return stopTask.WaitAsync(cancellationToken);
    }

    internal bool IsInCurrentDelivery => _currentDelivery.Value is { } delivery && Volatile.Read(ref delivery.Active);

    internal Task StopAndDrainAsync() => BeginStop();

    private Task BeginStop()
    {
        Task stopTask;
        TaskCompletionSource? completion = null;
        lock (_stateLock)
        {
            if (_stopTask is null)
            {
                if (!IsActive && _consumer is null)
                    return Task.CompletedTask;

                IsActive = false;
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _stopTask = completion.Task;
            }
            stopTask = _stopTask;
        }

        if (completion is not null)
            _ = StopCoreAsync(completion);

        return stopTask;
    }

    private async Task StopCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            _logger.LogInformation("Stopping consumer {ConsumerId}", ConsumerId);
            if (_startCompleted is not null)
                await _startCompleted.Task.ConfigureAwait(false);

            if (!string.IsNullOrEmpty(_consumerTag) && _channel.IsOpen)
            {
                await _channel.BasicCancelAsync(_consumerTag).ConfigureAwait(false);
                Task? unregistered;
                lock (_stateLock)
                    unregistered = _consumerUnregistered?.Task;
                if (unregistered is not null)
                    await unregistered.ConfigureAwait(false);
            }

            Task? deliveriesDrained;
            lock (_stateLock)
                deliveriesDrained = _deliveriesDrained?.Task;
            if (deliveriesDrained is not null)
                await deliveriesDrained.ConfigureAwait(false);

            if (_consumer is not null)
            {
                _consumer.ReceivedAsync -= OnMessageReceivedAsync;
                _consumer.RegisteredAsync -= OnConsumerRegisteredAsync;
                _consumer.UnregisteredAsync -= OnConsumerUnregisteredAsync;
                _consumer.ShutdownAsync -= OnConsumerShutdownAsync;
                _consumer = null;
            }
            _consumerTag = null;

            _logger.LogInformation("Consumer {ConsumerId} stopped. Processed: {Processed}, Failed: {Failed}",
                ConsumerId, _messagesProcessed, _messagesFailed);
            completion.SetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping consumer {ConsumerId}", ConsumerId);
            completion.SetException(ex);
        }
    }

    /// <inheritdoc/>
    public ConsumerMetrics GetMetrics()
    {
        return new ConsumerMetrics
        {
            MessagesProcessed = _messagesProcessed,
            MessagesFailed = _messagesFailed,
            AverageProcessingDuration = TimeSpan.Zero // Could track with stopwatch
        };
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        TaskCompletionSource? completion = null;
        lock (_stateLock)
        {
            if (_disposeTask is null)
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = completion.Task;
            }
            disposeTask = _disposeTask;
        }

        if (completion is not null)
            _ = DisposeCoreAsync(completion);

        // Defer closing the channel until this callback has settled its delivery.
        return IsInCurrentDelivery
            ? ValueTask.CompletedTask
            : new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            try
            {
                await BeginStop().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (_channel.IsOpen)
                        await _channel.CloseAsync().ConfigureAwait(false);
                    _channel.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing channel for consumer {ConsumerId}", ConsumerId);
                }

                _transport.RemoveConsumer(ConsumerId);
            }

            _logger.LogDebug("Consumer {ConsumerId} disposed", ConsumerId);
            completion.SetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing consumer {ConsumerId}", ConsumerId);
            completion.SetException(ex);
        }
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var previousDelivery = _currentDelivery.Value;
        var delivery = new DeliveryScope();
        _currentDelivery.Value = delivery;
        lock (_stateLock)
        {
            if (_inFlightDeliveries++ == 0)
                _deliveriesDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        try
        {
            await ProcessMessageAsync(ea).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref delivery.Active, false);
            _currentDelivery.Value = previousDelivery;
            lock (_stateLock)
            {
                if (--_inFlightDeliveries == 0)
                {
                    _deliveriesDrained!.SetResult();
                    _deliveriesDrained = null;
                }
            }
        }
    }

    private async Task ProcessMessageAsync(BasicDeliverEventArgs ea)
    {
        var messageId = ea.BasicProperties.MessageId ?? string.Empty;
        var dispositionStarted = 0;

        async Task SettleAsync(bool? requeue, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Interlocked.CompareExchange(ref dispositionStarted, 1, 0) != 0)
                throw new InvalidOperationException($"Message {messageId} has already been settled");

            var settled = false;
            try
            {
                // Once started, disposition must not be canceled midway through an uncertain broker write.
                if (requeue is bool shouldRequeue)
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, shouldRequeue).ConfigureAwait(false);
                else
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false).ConfigureAwait(false);

                settled = true;
            }
            finally
            {
                // Closing the channel releases any unsettled delivery without risking a duplicate ack.
                if (!settled && _channel.IsOpen)
                {
                    try
                    {
                        await _channel.CloseAsync().ConfigureAwait(false);
                    }
                    catch (Exception closeError)
                    {
                        _logger.LogWarning(closeError, "Could not close channel after disposition failure for {MessageId}", messageId);
                    }
                }
            }
        }

        _logger.LogTrace("Received message {MessageId} from {Queue}", messageId, Source.Name);

        var startTime = _timeProvider.GetTimestamp();
        Activity? activity = null;

        try
        {
            _instrumentation.AddEvent(activity, "receive.start");

            // Build transport envelope with headers including trace context
            var headers = ea.BasicProperties.Headers?
                .Where(static kvp => kvp.Value is not null)
                .ToImmutableDictionary(
                    static kvp => kvp.Key,
                    static kvp => kvp.Value!)
                ?? ImmutableDictionary<string, object>.Empty;

            var envelope = new TransportEnvelope
            {
                MessageId = messageId,
                CorrelationId = ea.BasicProperties.CorrelationId,
                ContentType = ea.BasicProperties.ContentType ?? "application/octet-stream",
                Body = ea.Body.ToArray(),
                MessageType = ea.BasicProperties.Type ?? "Unknown",
                Headers = headers
            };

            // Extract trace context from message headers
            var parentContext = _instrumentation.ExtractTraceContext(envelope);

            // Start receive activity with extracted parent context
            activity = _instrumentation.StartReceiveActivity(
                envelope,
                Source.Name,
                _transport.Name,
                ConsumerId,
                parentContext);

            _instrumentation.AddEvent(activity, "deserialization.start");

            // Build message context
            var context = new MessageContext
            {
                TransportName = _transport.Name,
                SourceAddress = Source,
                ReceiveTimestamp = _timeProvider.GetUtcNow(),
                Properties = new Dictionary<string, object>
                {
                    ["DeliveryTag"] = ea.DeliveryTag,
                    ["Redelivered"] = ea.Redelivered,
                    ["Exchange"] = ea.Exchange ?? string.Empty,
                    ["RoutingKey"] = ea.RoutingKey ?? string.Empty,
                    ["MessageId"] = envelope.MessageId,
                    ["CorrelationId"] = envelope.CorrelationId ?? string.Empty
                }.ToImmutableDictionary(),
                Acknowledge = async (ct) =>
                {
                    await SettleAsync(null, ct).ConfigureAwait(false);
                    _instrumentation.AddEvent(activity, "acknowledge");
                },
                Reject = async (requeue, ct) =>
                {
                    await SettleAsync(requeue, ct).ConfigureAwait(false);
                    _instrumentation.AddEvent(activity, requeue ? "reject.requeue" : "reject.drop");
                },
                Defer = async (delay, ct) =>
                {
                    await SettleAsync(true, ct).ConfigureAwait(false);
                    _instrumentation.AddEvent(activity, "defer");
                },
                DeadLetter = async (reason, ct) =>
                {
                    await SettleAsync(false, ct).ConfigureAwait(false);
                    _instrumentation.AddEvent(activity, "deadletter",
                    [
                        new KeyValuePair<string, object?>("reason", reason ?? "unknown")
                    ]);
                }
            };

            _instrumentation.AddEvent(activity, "deserialization.complete");
            _instrumentation.AddEvent(activity, "handler.start");

            // Process message
            await _handler(envelope, context, CancellationToken.None).ConfigureAwait(false);

            _instrumentation.AddEvent(activity, "handler.complete");

            if (_options.AutoAcknowledge && Volatile.Read(ref dispositionStarted) == 0)
                await SettleAsync(null, CancellationToken.None).ConfigureAwait(false);

            Interlocked.Increment(ref _messagesProcessed);

            // Record successful operation
            var durationMs = _timeProvider.GetElapsedTime(startTime).TotalMilliseconds;
            _instrumentation.RecordReceiveDuration(_transport.Name, Source.Name, envelope.MessageType, durationMs);
            _instrumentation.RecordOperation(_transport.Name, "receive", "success");

            _logger.LogTrace("Successfully processed message {MessageId}", messageId);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _messagesFailed);

            // Record error
            _instrumentation.RecordError(activity, ex);
            _instrumentation.RecordOperation(_transport.Name, "receive", "failure");

            _logger.LogError(ex, "Error processing message {MessageId} from {Queue}", messageId, Source.Name);

            if (Volatile.Read(ref dispositionStarted) == 0)
                await SettleAsync(_options.RequeueOnFailure, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            activity?.Dispose();
        }
    }

    private Task OnConsumerShutdownAsync(object? sender, ShutdownEventArgs e)
    {
        _logger.LogWarning(
            "Consumer {ConsumerId} shutdown: ReplyCode: {ReplyCode}, ReplyText: {ReplyText}",
            ConsumerId, e.ReplyCode, e.ReplyText);

        lock (_stateLock)
        {
            IsActive = false;
            _consumerUnregistered?.TrySetResult();
        }
        return Task.CompletedTask;
    }

    private Task OnConsumerUnregisteredAsync(object? sender, ConsumerEventArgs e)
    {
        lock (_stateLock)
        {
            IsActive = false;
            _consumerUnregistered?.TrySetResult();
        }
        return Task.CompletedTask;
    }

    private Task OnConsumerRegisteredAsync(object? sender, ConsumerEventArgs e)
    {
        lock (_stateLock)
        {
            if (_consumerUnregistered?.Task.IsCompleted == true)
                _consumerUnregistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_stopTask is null)
                IsActive = true;
        }
        return Task.CompletedTask;
    }

    private sealed class DeliveryScope
    {
        public bool Active = true;
    }
}
