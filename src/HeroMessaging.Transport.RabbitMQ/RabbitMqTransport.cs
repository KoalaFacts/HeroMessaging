using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ.Connection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace HeroMessaging.Transport.RabbitMQ;

/// <summary>
/// RabbitMQ implementation of IMessageTransport
/// </summary>
public sealed class RabbitMqTransport : IMessageTransport
{
    private readonly RabbitMqTransportOptions _options;
    private readonly ILogger<RabbitMqTransport> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITransportInstrumentation _instrumentation;
    private RabbitMqConnectionPool? _connectionPool;
    private readonly ConcurrentDictionary<string, RabbitMqChannelPool> _channelPools = new();
    private readonly ConcurrentDictionary<string, RabbitMqConsumer> _consumers = new();
    private TransportState _state = TransportState.Disconnected;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly TimeProvider _timeProvider;

    /// <inheritdoc/>
    public string Name => _options.Name;

    /// <inheritdoc/>
    public TransportState State => _state;

    /// <inheritdoc/>
    public event EventHandler<TransportStateChangedEventArgs>? StateChanged;

    /// <inheritdoc/>
    public event EventHandler<TransportErrorEventArgs>? Error;

    public RabbitMqTransport(
        RabbitMqTransportOptions options,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        ITransportInstrumentation? instrumentation = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<RabbitMqTransport>();
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _instrumentation = instrumentation ?? NoOpTransportInstrumentation.Instance;

        _logger.LogInformation(
            "RabbitMQ transport created. Name: {Name}, Host: {Host}, Port: {Port}, VirtualHost: {VirtualHost}",
            _options.Name, _options.Host, _options.Port, _options.VirtualHost);
    }

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_state == TransportState.Connected)
                return;

            ChangeState(TransportState.Connecting, "Connecting to RabbitMQ");

            _logger.LogInformation("Connecting to RabbitMQ at {Host}:{Port}/{VirtualHost}",
                _options.Host, _options.Port, _options.VirtualHost);

            // Create connection pool
            _connectionPool = new RabbitMqConnectionPool(
                _options,
                _loggerFactory.CreateLogger<RabbitMqConnectionPool>(),
                _timeProvider);

            // Test connection by acquiring one
            var connection = await _connectionPool.GetConnectionAsync(cancellationToken);
            _connectionPool.ReleaseConnection(connection);

            ChangeState(TransportState.Connected, "Connected to RabbitMQ");

            _logger.LogInformation("Successfully connected to RabbitMQ");
        }
        catch (Exception ex)
        {
            ChangeState(TransportState.Failed, $"Failed to connect to RabbitMQ: {ex.Message}");
            OnError(ex, "Connection failed");
            throw;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(TransportState.Disconnecting, "Disconnecting from RabbitMQ");

        _logger.LogInformation("Disconnecting from RabbitMQ");

        // Stop all consumers
        foreach (var consumer in _consumers.Values)
        {
            await consumer.StopAsync(cancellationToken);
        }
        _consumers.Clear();

        // Dispose channel pools
        foreach (var channelPool in _channelPools.Values)
        {
            await channelPool.DisposeAsync();
        }
        _channelPools.Clear();

        // Dispose connection pool
        if (_connectionPool != null)
        {
            await _connectionPool.DisposeAsync();
            _connectionPool = null;
        }

        ChangeState(TransportState.Disconnected, "Disconnected from RabbitMQ");

        _logger.LogInformation("Disconnected from RabbitMQ");
    }

    /// <inheritdoc/>
    public async Task SendAsync(
        TransportAddress destination,
        TransportEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Start send activity for distributed tracing
        using var activity = _instrumentation.StartSendActivity(envelope, destination.Name, Name);
        var startTime = _timeProvider.GetTimestamp();

        try
        {
            // Inject trace context into envelope headers
            envelope = _instrumentation.InjectTraceContext(envelope, activity);

            var channelPool = await GetOrCreateChannelPoolAsync(cancellationToken);

            await channelPool.ExecuteAsync(async channel =>
            {
                // Enable publisher confirms if configured
                if (_options.UsePublisherConfirms)
                {
                    channel.ConfirmSelect();
                }

                _instrumentation.AddEvent(activity, "serialization.start");

                // Build message properties
                var properties = channel.CreateBasicProperties();
                properties.Persistent = true; // Durable messages
                properties.MessageId = envelope.MessageId;
                properties.CorrelationId = envelope.CorrelationId;
                properties.ContentType = envelope.ContentType ?? "application/octet-stream";
                properties.Timestamp = new AmqpTimestamp(_timeProvider.GetUtcNow().ToUnixTimeSeconds());

                // Copy headers including trace context
                if (envelope.Headers.Count > 0)
                {
                    properties.Headers = new Dictionary<string, object>(envelope.Headers);
                }

                _instrumentation.AddEvent(activity, "serialization.complete", new[]
                {
                    new KeyValuePair<string, object?>("size_bytes", envelope.Body.Length)
                });

                _instrumentation.AddEvent(activity, "publish.start");

                // Send to default exchange with queue name as routing key (direct routing)
                channel.BasicPublish(
                    exchange: "",
                    routingKey: destination.Name,
                    basicProperties: properties,
                    body: envelope.Body);

                // Wait for confirm if enabled
                if (_options.UsePublisherConfirms)
                {
                    var confirmed = channel.WaitForConfirms(_options.PublisherConfirmTimeout);
                    if (!confirmed)
                    {
                        _instrumentation.AddEvent(activity, "publish.timeout");
                        throw new TimeoutException(
                            $"Publisher confirm timed out after {_options.PublisherConfirmTimeout} for message {envelope.MessageId}");
                    }
                    _instrumentation.AddEvent(activity, "publish.confirmed");
                }

                _instrumentation.AddEvent(activity, "publish.complete");

                _logger.LogDebug("Sent message {MessageId} to queue {Queue}", envelope.MessageId, destination.Name);

                await Task.CompletedTask; // Keep async signature
            }, cancellationToken);

            // Record successful operation
            var durationMs = _timeProvider.GetElapsedTime(startTime).TotalMilliseconds;
            _instrumentation.RecordSendDuration(Name, destination.Name, envelope.MessageType, durationMs);
            _instrumentation.RecordOperation(Name, "send", "success");
        }
        catch (Exception ex)
        {
            // Record error
            _instrumentation.RecordError(activity, ex);
            _instrumentation.RecordOperation(Name, "send", "failure");

            _logger.LogError(ex, "Failed to send message {MessageId} to queue {Queue}", envelope.MessageId, destination.Name);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task PublishAsync(
        TransportAddress topic,
        TransportEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Start publish activity for distributed tracing
        using var activity = _instrumentation.StartPublishActivity(envelope, topic.Name, Name);
        var startTime = _timeProvider.GetTimestamp();

        try
        {
            // Inject trace context into envelope headers
            envelope = _instrumentation.InjectTraceContext(envelope, activity);

            var channelPool = await GetOrCreateChannelPoolAsync(cancellationToken);

            await channelPool.ExecuteAsync(async channel =>
            {
                // Enable publisher confirms if configured
                if (_options.UsePublisherConfirms)
                {
                    channel.ConfirmSelect();
                }

                _instrumentation.AddEvent(activity, "serialization.start");

                // Build message properties
                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.MessageId = envelope.MessageId;
                properties.CorrelationId = envelope.CorrelationId;
                properties.ContentType = envelope.ContentType ?? "application/octet-stream";
                properties.Timestamp = new AmqpTimestamp(_timeProvider.GetUtcNow().ToUnixTimeSeconds());

                // Copy headers including trace context
                if (envelope.Headers.Count > 0)
                {
                    properties.Headers = new Dictionary<string, object>(envelope.Headers);
                }

                _instrumentation.AddEvent(activity, "serialization.complete", new[]
                {
                    new KeyValuePair<string, object?>("size_bytes", envelope.Body.Length)
                });

                _instrumentation.AddEvent(activity, "publish.start");

                // Publish to topic exchange (use topic name as exchange)
                channel.BasicPublish(
                    exchange: topic.Name,
                    routingKey: envelope.RoutingKey ?? "#", // Broadcast by default
                    basicProperties: properties,
                    body: envelope.Body);

                // Wait for confirm if enabled
                if (_options.UsePublisherConfirms)
                {
                    var confirmed = channel.WaitForConfirms(_options.PublisherConfirmTimeout);
                    if (!confirmed)
                    {
                        _instrumentation.AddEvent(activity, "publish.timeout");
                        throw new TimeoutException(
                            $"Publisher confirm timed out after {_options.PublisherConfirmTimeout} for message {envelope.MessageId}");
                    }
                    _instrumentation.AddEvent(activity, "publish.confirmed");
                }

                _instrumentation.AddEvent(activity, "publish.complete");

                _logger.LogDebug("Published message {MessageId} to exchange {Exchange}", envelope.MessageId, topic.Name);

                await Task.CompletedTask; // Keep async signature
            }, cancellationToken);

            // Record successful operation
            var durationMs = _timeProvider.GetElapsedTime(startTime).TotalMilliseconds;
            _instrumentation.RecordSendDuration(Name, topic.Name, envelope.MessageType, durationMs);
            _instrumentation.RecordOperation(Name, "publish", "success");
        }
        catch (Exception ex)
        {
            // Record error
            _instrumentation.RecordError(activity, ex);
            _instrumentation.RecordOperation(Name, "publish", "failure");

            _logger.LogError(ex, "Failed to publish message {MessageId} to exchange {Exchange}", envelope.MessageId, topic.Name);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<ITransportConsumer> SubscribeAsync(
        TransportAddress source,
        Func<TransportEnvelope, MessageContext, CancellationToken, Task> handler,
        ConsumerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        options ??= ConsumerOptions.Default;
        var consumerId = options.ConsumerId ?? Guid.NewGuid().ToString();

        _logger.LogInformation("Creating consumer {ConsumerId} for {Source}", consumerId, source.Name);

        var channelPool = await GetOrCreateChannelPoolAsync(cancellationToken);
        var channel = await channelPool.AcquireChannelAsync(cancellationToken);

        // Set prefetch count
        channel.BasicQos(0, _options.PrefetchCount, false);

        var consumer = new RabbitMqConsumer(
            consumerId,
            source,
            channel,
            handler,
            options,
            this,
            _loggerFactory.CreateLogger<RabbitMqConsumer>(),
            _instrumentation);

        if (!_consumers.TryAdd(consumerId, consumer))
        {
            channel.Dispose();
            throw new InvalidOperationException($"Consumer '{consumerId}' already exists");
        }

        if (options.StartImmediately)
        {
            await consumer.StartAsync(cancellationToken);
        }

        return consumer;
    }

    /// <inheritdoc/>
    public async Task ConfigureTopologyAsync(
        TransportTopology topology,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        _logger.LogInformation("Configuring RabbitMQ topology");

        var channelPool = await GetOrCreateChannelPoolAsync(cancellationToken);

        await channelPool.ExecuteAsync(async channel =>
        {
            // Declare exchanges
            foreach (var exchange in topology.Exchanges)
            {
                _logger.LogDebug("Declaring exchange: {ExchangeName} ({Type})", exchange.Name, exchange.Type);

                var exchangeType = exchange.Type switch
                {
                    ExchangeType.Direct => "direct",
                    ExchangeType.Fanout => "fanout",
                    ExchangeType.Topic => "topic",
                    ExchangeType.Headers => "headers",
                    _ => "topic"
                };

                channel.ExchangeDeclare(
                    exchange: exchange.Name,
                    type: exchangeType,
                    durable: exchange.Durable,
                    autoDelete: exchange.AutoDelete,
                    arguments: exchange.Arguments);
            }

            // Declare queues
            foreach (var queue in topology.Queues)
            {
                _logger.LogDebug("Declaring queue: {QueueName}", queue.Name);

                var arguments = queue.Arguments ?? new Dictionary<string, object>();

                // Add dead letter exchange if specified
                if (!string.IsNullOrEmpty(queue.DeadLetterExchange))
                {
                    arguments["x-dead-letter-exchange"] = queue.DeadLetterExchange;
                    if (!string.IsNullOrEmpty(queue.DeadLetterRoutingKey))
                    {
                        arguments["x-dead-letter-routing-key"] = queue.DeadLetterRoutingKey;
                    }
                }

                // Add max length if specified
                if (queue.MaxLength.HasValue)
                {
                    arguments["x-max-length"] = queue.MaxLength.Value;
                }

                // Add message TTL if specified
                if (queue.MessageTtl.HasValue)
                {
                    arguments["x-message-ttl"] = (int)queue.MessageTtl.Value.TotalMilliseconds;
                }

                // Add max priority if specified
                if (queue.MaxPriority.HasValue)
                {
                    arguments["x-max-priority"] = queue.MaxPriority.Value;
                }

                channel.QueueDeclare(
                    queue: queue.Name,
                    durable: queue.Durable,
                    exclusive: queue.Exclusive,
                    autoDelete: queue.AutoDelete,
                    arguments: arguments);
            }

            // Create bindings
            foreach (var binding in topology.Bindings)
            {
                _logger.LogDebug("Creating binding: {Exchange} -> {Queue} ({RoutingKey})",
                    binding.SourceExchange, binding.Destination, binding.RoutingKey ?? "(all)");

                channel.QueueBind(
                    queue: binding.Destination,
                    exchange: binding.SourceExchange,
                    routingKey: binding.RoutingKey ?? "",
                    arguments: binding.Arguments);
            }

            await Task.CompletedTask; // Keep async signature
        }, cancellationToken);

        _logger.LogInformation("Topology configured successfully");
    }

    /// <inheritdoc/>
    public async Task<TransportHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var isHealthy = _state == TransportState.Connected && _connectionPool != null;

        var health = new TransportHealth
        {
            TransportName = Name,
            Status = isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy,
            State = _state,
            StatusMessage = isHealthy ? "RabbitMQ transport is healthy" : $"Transport state: {_state}",
            Timestamp = _timeProvider.GetUtcNow().DateTime,
            Duration = TimeSpan.Zero,
            ActiveConsumers = _consumers.Count,
            Data = new Dictionary<string, object>
            {
                ["Host"] = _options.Host,
                ["Port"] = _options.Port,
                ["VirtualHost"] = _options.VirtualHost,
                ["ConsumerCount"] = _consumers.Count
            }
        };

        if (_connectionPool != null)
        {
            var stats = _connectionPool.GetStatistics();
            health.ActiveConnections = stats.Total;
            health.Data["ConnectionsActive"] = stats.Active;
            health.Data["ConnectionsIdle"] = stats.Idle;
        }

        await Task.CompletedTask; // Keep async signature
        return health;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _connectLock.Dispose();
    }

    internal void RemoveConsumer(string consumerId)
    {
        _consumers.TryRemove(consumerId, out _);
    }

    private async Task<RabbitMqChannelPool> GetOrCreateChannelPoolAsync(CancellationToken cancellationToken)
    {
        if (_connectionPool == null)
            throw new InvalidOperationException("Connection pool is not initialized");

        var connection = await _connectionPool.GetConnectionAsync(cancellationToken);

        var poolKey = connection.ClientProvidedName ?? "default";

        return _channelPools.GetOrAdd(poolKey, _ => new RabbitMqChannelPool(
            connection,
            maxChannels: _options.MaxChannelsPerConnection,
            channelLifetime: _options.ChannelLifetime,
            _loggerFactory.CreateLogger<RabbitMqChannelPool>(),
            _timeProvider));
    }

    private void EnsureConnected()
    {
        if (_state != TransportState.Connected)
        {
            throw new InvalidOperationException($"Transport is not connected. Current state: {_state}");
        }
    }

    private void ChangeState(TransportState newState, string? reason = null)
    {
        TransportState oldState;
        lock (_stateLock)
        {
            oldState = _state;
            _state = newState;
        }

        if (oldState != newState)
        {
            StateChanged?.Invoke(this, new TransportStateChangedEventArgs(oldState, newState, reason));
        }
    }

    private void OnError(Exception exception, string? context = null)
    {
        Error?.Invoke(this, new TransportErrorEventArgs(exception, context));
    }
}
