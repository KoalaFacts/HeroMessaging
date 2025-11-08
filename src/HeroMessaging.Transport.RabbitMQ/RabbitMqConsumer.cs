using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Threading;

namespace HeroMessaging.Transport.RabbitMQ;

/// <summary>
/// RabbitMQ implementation of ITransportConsumer
/// </summary>
internal sealed class RabbitMqConsumer : ITransportConsumer
{
    private readonly IModel _channel;
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
        IModel channel,
        Func<TransportEnvelope, MessageContext, CancellationToken, Task> handler,
        ConsumerOptions options,
        RabbitMqTransport transport,
        ILogger<RabbitMqConsumer> logger,
        ITransportInstrumentation? instrumentation = null)
    {
        ConsumerId = consumerId ?? throw new ArgumentNullException(nameof(consumerId));
        Source = source ?? throw new ArgumentNullException(nameof(source));
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
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isActive)
            {
                _logger.LogWarning("Consumer {ConsumerId} is already active", ConsumerId);
                return Task.CompletedTask;
            }

            _logger.LogInformation("Starting consumer {ConsumerId} for {Source}", ConsumerId, Source.Name);

            // Create async consumer
            _consumer = new AsyncEventingBasicConsumer(_channel);
            _consumer.Received += OnMessageReceived;
            _consumer.Shutdown += OnConsumerShutdown;

            // Start consuming
            _consumerTag = _channel.BasicConsume(
                queue: Source.Name,
                autoAck: false, // Manual acknowledgment for reliability
                consumer: _consumer);

            _isActive = true;

            _logger.LogInformation("Consumer {ConsumerId} started with tag {ConsumerTag}", ConsumerId, _consumerTag);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (!_isActive)
            {
                return Task.CompletedTask;
            }

            _logger.LogInformation("Stopping consumer {ConsumerId}", ConsumerId);

            if (_consumer != null)
            {
                _consumer.Received -= OnMessageReceived;
                _consumer.Shutdown -= OnConsumerShutdown;
            }

            if (!string.IsNullOrEmpty(_consumerTag) && _channel.IsOpen)
            {
                try
                {
                    _channel.BasicCancel(_consumerTag);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error cancelling consumer {ConsumerId}", ConsumerId);
                }
            }

            _isActive = false;

            _logger.LogInformation("Consumer {ConsumerId} stopped. Processed: {Processed}, Failed: {Failed}",
                ConsumerId, _messagesProcessed, _messagesFailed);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ConsumerMetrics GetMetrics()
    {
        return new ConsumerMetrics
        {
            ConsumerId = ConsumerId,
            Source = Source.Name,
            IsActive = _isActive,
            MessagesProcessed = _messagesProcessed,
            MessagesFailed = _messagesFailed,
            MessagesPerSecond = 0, // Could calculate over time window
            AverageProcessingTime = TimeSpan.Zero // Could track
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
                _channel.Close();
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

    private async Task OnMessageReceived(object sender, BasicDeliverEventArgs ea)
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
                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                    _instrumentation.AddEvent(activity, "acknowledge");
                    await Task.CompletedTask;
                },
                Reject = async (requeue, ct) =>
                {
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue);
                    _instrumentation.AddEvent(activity, requeue ? "reject.requeue" : "reject.drop");
                    await Task.CompletedTask;
                },
                Defer = async (delay, ct) =>
                {
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
                    _instrumentation.AddEvent(activity, "defer");
                    await Task.CompletedTask;
                },
                DeadLetter = async (reason, ct) =>
                {
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                    _instrumentation.AddEvent(activity, "deadletter", new[]
                    {
                        new KeyValuePair<string, object?>("reason", reason ?? "unknown")
                    });
                    await Task.CompletedTask;
                }
            };

            _instrumentation.AddEvent(activity, "deserialization.complete");
            _instrumentation.AddEvent(activity, "handler.start");

            // Process message
            await _handler(envelope, context, CancellationToken.None);

            _instrumentation.AddEvent(activity, "handler.complete");

            // Acknowledge successful processing
            _channel.BasicAck(ea.DeliveryTag, multiple: false);

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
            _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
        }
        finally
        {
            activity?.Dispose();
        }
    }

    private void OnConsumerShutdown(object? sender, ShutdownEventArgs e)
    {
        _logger.LogWarning(
            "Consumer {ConsumerId} shutdown: ReplyCode: {ReplyCode}, ReplyText: {ReplyText}",
            ConsumerId, e.ReplyCode, e.ReplyText);

        _isActive = false;
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
