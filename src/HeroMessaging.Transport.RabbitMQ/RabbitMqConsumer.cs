using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;

namespace HeroMessaging.Transport.RabbitMQ;

/// <summary>
/// RabbitMQ implementation of ITransportConsumer
/// </summary>
internal sealed class RabbitMqConsumer : ITransportConsumer
{
    private readonly IChannel _channel;
    private readonly Func<TransportEnvelope, MessageContext, CancellationToken, Task> _handler;
    private readonly ConsumerOptions _options;
    private readonly RabbitMqTransport _transport;
    private readonly ILogger<RabbitMqConsumer> _logger;
    private readonly ITransportInstrumentation _instrumentation;
    private AsyncEventingBasicConsumer? _consumer;
    private string? _consumerTag;
    private bool _isActive;
    private long _messagesProcessed;
    private long _messagesFailed;
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
    public bool IsActive => _isActive;

    public RabbitMqConsumer(
        string consumerId,
        TransportAddress source,
        IChannel channel,
        Func<TransportEnvelope, MessageContext, CancellationToken, Task> handler,
        ConsumerOptions options,
        RabbitMqTransport transport,
        ILogger<RabbitMqConsumer> logger,
        ITransportInstrumentation? instrumentation = null)
    {
        ConsumerId = consumerId ?? throw new ArgumentNullException(nameof(consumerId));
        if (string.IsNullOrEmpty(source.Name))
            throw new ArgumentException("Source address name cannot be null or empty", nameof(source));
        Source = source;
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _instrumentation = instrumentation ?? NoOpTransportInstrumentation.Instance;
    }

    /// <summary>
    /// Start consuming messages
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isActive)
            {
                _logger.LogWarning("Consumer {ConsumerId} is already active", ConsumerId);
                return;
            }

            _logger.LogInformation("Starting consumer {ConsumerId} for {Source}", ConsumerId, Source.Name);
            _isActive = true;
        }

        // Create async consumer outside the lock
        _consumer = new AsyncEventingBasicConsumer(_channel);
        _consumer.ReceivedAsync += OnMessageReceivedAsync;
        _consumer.ShutdownAsync += OnConsumerShutdownAsync;

        // Start consuming
        _consumerTag = await _channel.BasicConsumeAsync(
            queue: Source.Name,
            autoAck: false, // Manual acknowledgment for reliability
            consumer: _consumer);

        _logger.LogInformation("Consumer {ConsumerId} started with tag {ConsumerTag}", ConsumerId, _consumerTag);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (!_isActive)
            {
                return;
            }

            _logger.LogInformation("Stopping consumer {ConsumerId}", ConsumerId);

            if (_consumer != null)
            {
                _consumer.ReceivedAsync -= OnMessageReceivedAsync;
                _consumer.ShutdownAsync -= OnConsumerShutdownAsync;
            }

            _isActive = false;
        }

        if (!string.IsNullOrEmpty(_consumerTag) && _channel.IsOpen)
        {
            try
            {
                await _channel.BasicCancelAsync(_consumerTag);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cancelling consumer {ConsumerId}", ConsumerId);
            }
        }

        _logger.LogInformation("Consumer {ConsumerId} stopped. Processed: {Processed}, Failed: {Failed}",
            ConsumerId, _messagesProcessed, _messagesFailed);
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
    public async ValueTask DisposeAsync()
    {
        await StopAsync();

        // Dispose channel
        try
        {
            if (_channel.IsOpen)
            {
                await _channel.CloseAsync();
            }
            _channel.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing channel for consumer {ConsumerId}", ConsumerId);
        }

        // Remove from transport
        _transport.RemoveConsumer(ConsumerId);

        _logger.LogDebug("Consumer {ConsumerId} disposed", ConsumerId);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var messageId = ea.BasicProperties.MessageId;

        _logger.LogTrace("Received message {MessageId} from {Queue}", messageId, Source.Name);

        var startTime = Stopwatch.GetTimestamp();
        Activity? activity = null;

        try
        {
            _instrumentation.AddEvent(activity, "receive.start");

            // Build transport envelope with headers including trace context
            var headers = ea.BasicProperties.Headers?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value) ?? new Dictionary<string, object>();

            var envelope = new TransportEnvelope
            {
                MessageId = messageId,
                CorrelationId = ea.BasicProperties.CorrelationId,
                ContentType = ea.BasicProperties.ContentType,
                Body = ea.Body,
                MessageType = ea.BasicProperties.Type ?? "Unknown",
                Headers = headers.ToImmutableDictionary()
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
                ReceiveTimestamp = DateTime.UtcNow,
                Properties = new Dictionary<string, object>
                {
                    ["DeliveryTag"] = ea.DeliveryTag,
                    ["Redelivered"] = ea.Redelivered,
                    ["Exchange"] = ea.Exchange,
                    ["RoutingKey"] = ea.RoutingKey,
                    ["MessageId"] = envelope.MessageId,
                    ["CorrelationId"] = envelope.CorrelationId ?? string.Empty
                }.ToImmutableDictionary(),
                Acknowledge = async (ct) =>
                {
                    await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
                    _instrumentation.AddEvent(activity, "acknowledge");
                },
                Reject = async (requeue, ct) =>
                {
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue, ct);
                    _instrumentation.AddEvent(activity, requeue ? "reject.requeue" : "reject.drop");
                },
                Defer = async (delay, ct) =>
                {
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, ct);
                    _instrumentation.AddEvent(activity, "defer");
                },
                DeadLetter = async (reason, ct) =>
                {
                    await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
                    _instrumentation.AddEvent(activity, "deadletter", new[]
                    {
                        new KeyValuePair<string, object?>("reason", reason ?? "unknown")
                    });
                }
            };

            _instrumentation.AddEvent(activity, "deserialization.complete");
            _instrumentation.AddEvent(activity, "handler.start");

            // Process message
            await _handler(envelope, context, CancellationToken.None);

            _instrumentation.AddEvent(activity, "handler.complete");

            // Acknowledge successful processing
            await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);

            Interlocked.Increment(ref _messagesProcessed);

            // Record successful operation
            var durationMs = GetElapsedMilliseconds(startTime);
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

            // Nack with requeue for transient failures
            // In production, you'd want to check error type and potentially dead-letter permanent failures
            await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
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

        _isActive = false;
        return Task.CompletedTask;
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
