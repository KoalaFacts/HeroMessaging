using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

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
    private AsyncEventingBasicConsumer? _consumer;
    private string? _consumerTag;
    private bool _isActive;
    private long _messagesProcessed;
    private long _messagesFailed;
    private readonly object _stateLock = new();

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
        ILogger<RabbitMqConsumer> logger)
    {
        ConsumerId = consumerId ?? throw new ArgumentNullException(nameof(consumerId));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        try
        {
            // Build transport envelope
            var envelope = new TransportEnvelope
            {
                MessageId = Guid.TryParse(messageId, out var mid) ? mid : Guid.NewGuid(),
                CorrelationId = ea.BasicProperties.CorrelationId,
                ContentType = ea.BasicProperties.ContentType,
                Body = ea.Body.ToArray(),
                RoutingKey = ea.RoutingKey,
                Headers = ea.BasicProperties.Headers?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value) ?? new Dictionary<string, object>()
            };

            // Build message context
            var context = new MessageContext
            {
                MessageId = envelope.MessageId,
                CorrelationId = envelope.CorrelationId,
                Source = Source.Name,
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(ea.BasicProperties.Timestamp.UnixTime),
                Metadata = new Dictionary<string, object>
                {
                    ["DeliveryTag"] = ea.DeliveryTag,
                    ["Redelivered"] = ea.Redelivered,
                    ["Exchange"] = ea.Exchange,
                    ["RoutingKey"] = ea.RoutingKey
                }
            };

            // Process message
            await _handler(envelope, context, CancellationToken.None);

            // Acknowledge successful processing
            _channel.BasicAck(ea.DeliveryTag, multiple: false);

            Interlocked.Increment(ref _messagesProcessed);

            _logger.LogTrace("Successfully processed message {MessageId}", messageId);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _messagesFailed);

            _logger.LogError(ex, "Error processing message {MessageId} from {Queue}", messageId, Source.Name);

            // Nack with requeue for transient failures
            // In production, you'd want to check error type and potentially dead-letter permanent failures
            _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private void OnConsumerShutdown(object? sender, ShutdownEventArgs e)
    {
        _logger.LogWarning(
            "Consumer {ConsumerId} shutdown: ReplyCode: {ReplyCode}, ReplyText: {ReplyText}",
            ConsumerId, e.ReplyCode, e.ReplyText);

        _isActive = false;
    }
}
