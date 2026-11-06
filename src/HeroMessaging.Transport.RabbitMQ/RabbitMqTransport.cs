using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ.Connection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace HeroMessaging.Transport.RabbitMQ;

/// <summary>
/// RabbitMQ implementation of IMessageTransport
/// </summary>
public sealed class RabbitMqTransport : IConfirmedQueueTransport, IRabbitMqConsumerHost
{
    private readonly RabbitMqTransportOptions _options;
    private readonly ILogger<RabbitMqTransport> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ITransportInstrumentation _instrumentation;
    private RabbitMqConnectionPool? _connectionPool;
    private readonly ConcurrentDictionary<string, RabbitMqChannelPool> _channelPools = new();
    private readonly ConcurrentDictionary<string, RabbitMqConsumer> _consumers = new();
#if NET9_0_OR_GREATER
    private readonly Lock _stateLock = new();
#else
    private readonly object _stateLock = new();
#endif
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private Task? _disconnectTask;
    private readonly TimeProvider _timeProvider;

    /// <inheritdoc/>
    public string Name => _options.Name;

    /// <inheritdoc/>
    public TransportState State { get; private set; } = TransportState.Disconnected;

    /// <inheritdoc/>
    public event EventHandler<TransportStateChangedEventArgs>? StateChanged;

    /// <inheritdoc/>
    public event EventHandler<TransportErrorEventArgs>? Error;
    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqTransport"/> class.
    /// </summary>

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
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == TransportState.Connected)
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
            var connection = await _connectionPool.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            _connectionPool.ReleaseConnection(connection);

            ChangeState(TransportState.Connected, "Connected to RabbitMQ");

            _logger.LogInformation("Successfully connected to RabbitMQ");
        }
        catch (Exception ex)
        {
            ChangeState(TransportState.Faulted, $"Failed to connect to RabbitMQ: {ex.Message}");
            OnError(ex, "Connection failed");
            var failedPool = _connectionPool;
            _connectionPool = null;
            if (failedPool is not null)
                await failedPool.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <inheritdoc/>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var calledFromHandler = _consumers.Values.Any(static consumer => consumer.IsInCurrentDelivery);
        Task disconnectTask;
        lock (_stateLock)
        {
            if (_disconnectTask is null || _disconnectTask.IsCompleted)
            {
                var stopTasks = _consumers.Values.Select(static consumer => consumer.StopAndDrainAsync()).ToArray();
                _disconnectTask = Task.Run(() => DisconnectCoreAsync(stopTasks));
            }
            disconnectTask = _disconnectTask;
        }

        if (calledFromHandler)
            return Task.CompletedTask;

        // The cleanup keeps running even when a caller was already canceled.
        return cancellationToken.IsCancellationRequested
            ? Task.FromCanceled(cancellationToken)
            : disconnectTask.WaitAsync(cancellationToken);
    }

    private async Task DisconnectCoreAsync(Task[] stopTasks)
    {
        ChangeState(TransportState.Disconnecting, "Disconnecting from RabbitMQ");
        _logger.LogInformation("Disconnecting from RabbitMQ");

        try
        {
            await Task.WhenAll(stopTasks).ConfigureAwait(false);
        }
        finally
        {
            _consumers.Clear();
            foreach (var channelPool in _channelPools.Values)
                await channelPool.DisposeAsync().ConfigureAwait(false);
            _channelPools.Clear();

            if (_connectionPool != null)
            {
                await _connectionPool.DisposeAsync().ConfigureAwait(false);
                _connectionPool = null;
            }
        }

        ChangeState(TransportState.Disconnected, "Disconnected from RabbitMQ");
        _logger.LogInformation("Disconnected from RabbitMQ");
    }

    /// <inheritdoc/>
    public Task SendAsync(
        TransportAddress destination,
        TransportEnvelope envelope,
        CancellationToken cancellationToken = default)
        => SendCoreAsync(destination, envelope, requireRoute: false, cancellationToken);

    /// <inheritdoc/>
    public Task SendConfirmedAsync(
        TransportAddress destination,
        TransportEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        if (!_options.UsePublisherConfirms)
            throw new InvalidOperationException("Confirmed queue delivery requires RabbitMQ publisher confirms.");

        return SendCoreAsync(destination, envelope, requireRoute: true, cancellationToken);
    }

    private async Task SendCoreAsync(
        TransportAddress destination,
        TransportEnvelope envelope,
        bool requireRoute,
        CancellationToken cancellationToken)
    {
        EnsureConnected();

        // Start send activity for distributed tracing
        using var activity = _instrumentation.StartSendActivity(envelope, destination.Name, Name);
        var startTime = _timeProvider.GetTimestamp();

        try
        {
            // Inject trace context into envelope headers
            envelope = _instrumentation.InjectTraceContext(envelope, activity);

            var channelPool = await GetOrCreateChannelPoolAsync(cancellationToken).ConfigureAwait(false);

            await channelPool.ExecuteAsync(async channel =>
            {
                _instrumentation.AddEvent(activity, "serialization.start");

                // Build message properties
                var properties = new BasicProperties
                {
                    Persistent = true, // Durable messages
                    MessageId = envelope.MessageId,
                    Type = envelope.MessageType,
                    CorrelationId = envelope.CorrelationId,
                    ContentType = envelope.ContentType ?? "application/octet-stream",
                    Timestamp = new AmqpTimestamp(_timeProvider.GetUtcNow().ToUnixTimeSeconds())
                };

                // Copy headers including trace context
                if (envelope.Headers.Count > 0)
                {
                    properties.Headers = ToNullableArguments(envelope.Headers);
                }

                _instrumentation.AddEvent(activity, "serialization.complete",
                [
                    new KeyValuePair<string, object?>("size_bytes", envelope.Body.Length)
                ]);

                _instrumentation.AddEvent(activity, "publish.start");

                // Send to default exchange with queue name as routing key (direct routing)
                await PublishMessageAsync(channel, "", destination.Name, properties, envelope.Body, cancellationToken, requireRoute)
                    .ConfigureAwait(false);

                if (_options.UsePublisherConfirms)
                {
                    _instrumentation.AddEvent(activity, "publish.confirmed");
                }

                _instrumentation.AddEvent(activity, "publish.complete");

                _logger.LogDebug("Sent message {MessageId} to queue {Queue}", envelope.MessageId, destination.Name);
            }, cancellationToken).ConfigureAwait(false);

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

            var channelPool = await GetOrCreateChannelPoolAsync(cancellationToken).ConfigureAwait(false);

            await channelPool.ExecuteAsync(async channel =>
            {
                _instrumentation.AddEvent(activity, "serialization.start");

                // Build message properties
                var properties = new BasicProperties
                {
                    Persistent = true,
                    MessageId = envelope.MessageId,
                    Type = envelope.MessageType,
                    CorrelationId = envelope.CorrelationId,
                    ContentType = envelope.ContentType ?? "application/octet-stream",
                    Timestamp = new AmqpTimestamp(_timeProvider.GetUtcNow().ToUnixTimeSeconds())
                };

                // Copy headers including trace context
                if (envelope.Headers.Count > 0)
                {
                    properties.Headers = ToNullableArguments(envelope.Headers);
                }

                _instrumentation.AddEvent(activity, "serialization.complete",
                [
                    new KeyValuePair<string, object?>("size_bytes", envelope.Body.Length)
                ]);

                _instrumentation.AddEvent(activity, "publish.start");

                // Publish to topic exchange (use topic name as exchange)
                // Try to get routing key from headers, default to "#" (broadcast)
                var routingKey = envelope.Headers.TryGetValue("RoutingKey", out var rk) ? rk?.ToString() ?? "#" : "#";

                await PublishMessageAsync(channel, topic.Name, routingKey, properties, envelope.Body, cancellationToken)
                    .ConfigureAwait(false);

                if (_options.UsePublisherConfirms)
                {
                    _instrumentation.AddEvent(activity, "publish.confirmed");
                }

                _instrumentation.AddEvent(activity, "publish.complete");

                _logger.LogDebug("Published message {MessageId} to exchange {Exchange}", envelope.MessageId, topic.Name);
            }, cancellationToken).ConfigureAwait(false);

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

        var channelPool = await GetOrCreateChannelPoolAsync(cancellationToken).ConfigureAwait(false);
        var channel = await channelPool.AcquireChannelAsync(cancellationToken).ConfigureAwait(false);

        RabbitMqConsumer? consumer = null;
        var registered = false;
        var subscribedSuccessfully = false;
        try
        {
            // A failed QoS setup still leaves an acquired channel to dispose.
            await channel.BasicQosAsync(0, _options.PrefetchCount, false, cancellationToken).ConfigureAwait(false);

            consumer = new RabbitMqConsumer(
                consumerId,
                source,
                channel,
                handler,
                options,
                this,
                _loggerFactory.CreateLogger<RabbitMqConsumer>(),
                _timeProvider,
                _instrumentation,
                channelPool.RetireConsumerChannel);

            if (!_consumers.TryAdd(consumerId, consumer))
                throw new InvalidOperationException($"Consumer '{consumerId}' already exists");

            registered = true;
            if (options.StartImmediately)
                await consumer.StartAsync(cancellationToken).ConfigureAwait(false);

            subscribedSuccessfully = true;
            return consumer;
        }
        finally
        {
            if (!subscribedSuccessfully)
            {
                try
                {
                    if (registered)
                        await consumer!.DisposeAsync().ConfigureAwait(false);
                    else
                        channel.Dispose();
                }
                catch (Exception cleanupError)
                {
                    // Do not replace the original startup or cancellation failure.
                    _logger.LogWarning(cleanupError,
                        "Failed cleaning up consumer {ConsumerId} after subscription failure", consumerId);
                }
            }
        }
    }

    /// <summary>
    /// Moves messages retained in a dead-letter queue back to the queue that rejected them (or to
    /// <see cref="DeadLetterReplayOptions.TargetQueue"/>) once the cause of the failure is fixed.
    /// </summary>
    /// <param name="deadLetterQueue">The queue bound to the work queue's dead-letter exchange.</param>
    /// <param name="options">Replay limits and target; <see langword="null"/> uses the defaults.</param>
    /// <param name="cancellationToken">
    /// Stops the replay. Messages already replayed stay replayed; the others stay in the dead-letter queue and
    /// <see cref="OperationCanceledException"/> is thrown.
    /// </param>
    /// <returns>How many messages were replayed and how many were left in place.</returns>
    /// <remarks>
    /// <para>
    /// A call looks at no more messages than the dead-letter queue held when it started. It first takes up to
    /// <see cref="DeadLetterReplayOptions.MaxMessages"/> replayable messages (held unacknowledged), then publishes each
    /// to its target queue with mandatory routing and a publisher confirmation and removes it from the dead-letter
    /// queue only after the broker confirms it. Taking the batch first means a message that fails again during the
    /// call cannot be replayed twice by it, even in a priority queue.
    /// </para>
    /// <para>
    /// Messages are counted as <see cref="DeadLetterReplayResult.Unresolved"/> and moved to the back of the
    /// dead-letter queue (republished with confirmation, not requeued, so a quorum queue's delivery limit never
    /// drops them; in a priority queue they keep their priority) when their <c>x-death</c> history names no queue, names the dead-letter queue itself, or names a
    /// queue that no longer routes (deleted or renamed). Replay those with <see cref="DeadLetterReplayOptions.TargetQueue"/>.
    /// An explicit target that does not route throws <see cref="QueueDeliveryException"/> and leaves every message
    /// not yet replayed in the dead-letter queue.
    /// </para>
    /// <para>
    /// Delivery is at least once: if the process stops, the connection drops or the confirmation times out after
    /// the broker accepted the copy, the dead-letter copy stays too and a later call replays it again. The same
    /// windows can leave an unresolved message twice in the dead-letter queue. Handlers
    /// must tolerate duplicates, as they already must for redelivery.
    /// </para>
    /// <para>
    /// The copy keeps the body, properties and <c>x-death</c> history, drops the broker's <c>x-delivery-count</c> so
    /// it starts with the consumer's full retry budget, drops <c>UserId</c> (the broker would reject a copy claiming
    /// another user) and carries a <c>hero-replay-count</c> header counting replays.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The dead-letter queue name is blank, or the target is blank or is the dead-letter queue.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="DeadLetterReplayOptions.MaxMessages"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">The transport is not connected, publisher confirms are disabled, or the dead-letter queue does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled; messages not yet replayed stay in the dead-letter queue.</exception>
    /// <exception cref="QueueDeliveryException">The explicit <see cref="DeadLetterReplayOptions.TargetQueue"/> does not route.</exception>
    /// <exception cref="TimeoutException">The broker did not confirm a copy within <see cref="RabbitMqTransportOptions.PublisherConfirmTimeout"/>.</exception>
    public async Task<DeadLetterReplayResult> ReplayDeadLettersAsync(
        TransportAddress deadLetterQueue,
        DeadLetterReplayOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deadLetterQueue.Name, nameof(deadLetterQueue));
        options ??= new DeadLetterReplayOptions();
        if (options.MaxMessages <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.MaxMessages, "DeadLetterReplayOptions.MaxMessages must be positive; it bounds the messages taken per call.");
        }

        if (options.TargetQueue is not null &&
            (string.IsNullOrWhiteSpace(options.TargetQueue) || options.TargetQueue == deadLetterQueue.Name))
        {
            throw new ArgumentException(
                "DeadLetterReplayOptions.TargetQueue must name a queue other than the dead-letter queue, or be null to use each message's x-death source queue.",
                nameof(options));
        }

        if (!_options.UsePublisherConfirms)
        {
            throw new InvalidOperationException(
                $"Dead-letter replay removes a message only after the broker confirms its copy. Enable {nameof(RabbitMqTransportOptions.UsePublisherConfirms)}.");
        }

        EnsureConnected();

        var channelPool = await GetOrCreateChannelPoolAsync(cancellationToken).ConfigureAwait(false);
        var channel = await channelPool.AcquireChannelAsync(cancellationToken).ConfigureAwait(false);
        var replayed = 0;
        var unresolved = 0;
        var settled = false;
        try
        {
            // Take the whole batch before publishing anything, so a replayed message that fails again and returns
            // (ahead of older messages in a priority queue) cannot be fetched twice by this call.
            var batch = new List<(BasicGetResult Message, string Target)>();
            var unresolvedHeld = new List<BasicGetResult>();
            var depth = await GetQueueDepthAsync(channel, deadLetterQueue.Name, cancellationToken).ConfigureAwait(false);
            for (long taken = 0; taken < depth && batch.Count < options.MaxMessages; taken++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var message = await channel.BasicGetAsync(deadLetterQueue.Name, autoAck: false, cancellationToken).ConfigureAwait(false);
                if (message is null)
                    break;

                if (ResolveReplayTarget(deadLetterQueue.Name, options.TargetQueue, message) is { } target)
                {
                    batch.Add((message, target));
                }
                else
                {
                    // Held until the fetch ends: moved now, a high-priority copy would be fetched again at once.
                    unresolvedHeld.Add(message);
                }
            }

            foreach (var message in unresolvedHeld)
            {
                await MoveToBackAsync(channel, deadLetterQueue.Name, message).ConfigureAwait(false);
                unresolved++;
            }

            foreach (var (message, target) in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await TryReplayAsync(channel, deadLetterQueue.Name, target, options.TargetQueue is null, message).ConfigureAwait(false))
                {
                    await channel.BasicAckAsync(message.DeliveryTag, multiple: false, CancellationToken.None).ConfigureAwait(false);
                    replayed++;
                }
                else
                {
                    await MoveToBackAsync(channel, deadLetterQueue.Name, message).ConfigureAwait(false);
                    unresolved++;
                }
            }

            settled = true;
        }
        finally
        {
            try
            {
                if (!settled && channel.IsOpen)
                {
                    // Closing the channel returns every unacknowledged message to the dead-letter queue.
                    try
                    {
                        await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception closeError) when (closeError is AlreadyClosedException or OperationInterruptedException or IOException or TimeoutException or OperationCanceledException or ObjectDisposedException)
                    {
                        _logger.LogWarning(closeError, "Could not close the replay channel for {DeadLetterQueue}", deadLetterQueue.Name);
                    }
                }
            }
            finally
            {
                channelPool.ReleaseChannel(channel);
            }
        }

        return new DeadLetterReplayResult(replayed, unresolved);
    }

    private static async Task<long> GetQueueDepthAsync(IChannel channel, string queue, CancellationToken cancellationToken)
    {
        try
        {
            var declared = await channel.QueueDeclarePassiveAsync(queue, cancellationToken).ConfigureAwait(false);
            return declared.MessageCount;
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
        {
            throw new InvalidOperationException(
                $"Dead-letter queue '{queue}' does not exist. Check the name against the work queue's dead-letter exchange binding.", ex);
        }
    }

    private string? ResolveReplayTarget(string deadLetterQueue, string? explicitTarget, BasicGetResult message)
    {
        var target = explicitTarget ?? DeadLetterReplayHeaders.ResolveSourceQueue(message.BasicProperties.Headers);
        if (target is not null && target != deadLetterQueue)
            return target;

        _logger.LogWarning(
            "Leaving message {MessageId} in {DeadLetterQueue}: its x-death history names no other source queue. Replay it with DeadLetterReplayOptions.TargetQueue",
            message.BasicProperties.MessageId, deadLetterQueue);
        return null;
    }

    /// <summary>
    /// Moves an unresolved message to the back of the dead-letter queue as a new, confirmed message instead of
    /// requeueing it: a quorum queue counts a requeue against its delivery limit and could eventually drop a message
    /// that was never replayed.
    /// </summary>
    private async Task MoveToBackAsync(IChannel channel, string deadLetterQueue, BasicGetResult message)
    {
        var properties = new BasicProperties(message.BasicProperties)
        {
            Headers = DeadLetterReplayHeaders.WithoutDeliveryCount(message.BasicProperties.Headers),
            UserId = null
        };
        await PublishMessageAsync(channel, string.Empty, deadLetterQueue, properties, message.Body, CancellationToken.None, requireRoute: true)
            .ConfigureAwait(false);
        await channel.BasicAckAsync(message.DeliveryTag, multiple: false, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<bool> TryReplayAsync(IChannel channel, string deadLetterQueue, string target, bool targetFromHistory, BasicGetResult message)
    {
        var messageId = message.BasicProperties.MessageId;
        var properties = new BasicProperties(message.BasicProperties)
        {
            Headers = DeadLetterReplayHeaders.PrepareHeaders(message.BasicProperties.Headers),
            UserId = null
        };
        try
        {
            await PublishMessageAsync(channel, string.Empty, target, properties, message.Body, CancellationToken.None, requireRoute: true)
                .ConfigureAwait(false);
        }
        catch (QueueDeliveryException ex) when (targetFromHistory)
        {
            _logger.LogWarning(ex,
                "Leaving message {MessageId} in {DeadLetterQueue}: its source queue {Queue} does not route (deleted or renamed?). Replay it with DeadLetterReplayOptions.TargetQueue",
                messageId, deadLetterQueue, target);
            return false;
        }

        _logger.LogInformation("Replayed message {MessageId} from {DeadLetterQueue} to {Queue}", messageId, deadLetterQueue, target);
        return true;
    }

    /// <inheritdoc/>
    public async Task ConfigureTopologyAsync(
        TransportTopology topology,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        _logger.LogInformation("Configuring RabbitMQ topology");

        var channelPool = await GetOrCreateChannelPoolAsync(cancellationToken).ConfigureAwait(false);

        await channelPool.ExecuteAsync(async channel =>
        {
            // Declare exchanges
            foreach (var exchange in topology.Exchanges)
            {
                _logger.LogDebug("Declaring exchange: {ExchangeName} ({Type})", exchange.Name, exchange.Type);

                var exchangeType = exchange.Type switch
                {
                    Abstractions.Transport.ExchangeType.Direct => "direct",
                    Abstractions.Transport.ExchangeType.Fanout => "fanout",
                    Abstractions.Transport.ExchangeType.Topic => "topic",
                    Abstractions.Transport.ExchangeType.Headers => "headers",
                    _ => "topic"
                };

                await channel.ExchangeDeclareAsync(
                    exchange: exchange.Name,
                    type: exchangeType,
                    durable: exchange.Durable,
                    autoDelete: exchange.AutoDelete,
                    cancellationToken: cancellationToken,
                    arguments: ToNullableArguments(exchange.Arguments)).ConfigureAwait(false);
            }

            // Declare queues
            foreach (var queue in topology.Queues)
            {
                _logger.LogDebug("Declaring queue: {QueueName}", queue.Name);

                var arguments = ToNullableArguments(queue.Arguments);

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

                await channel.QueueDeclareAsync(
                    queue: queue.Name,
                    durable: queue.Durable,
                    exclusive: queue.Exclusive,
                    autoDelete: queue.AutoDelete,
                    arguments: arguments,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            // Create bindings
            foreach (var binding in topology.Bindings)
            {
                _logger.LogDebug("Creating binding: {Exchange} -> {Queue} ({RoutingKey})",
                    binding.SourceExchange, binding.Destination, binding.RoutingKey ?? "(all)");

                await channel.QueueBindAsync(
                    queue: binding.Destination,
                    exchange: binding.SourceExchange,
                    routingKey: binding.RoutingKey ?? "",
                    arguments: ToNullableArguments(binding.Arguments),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Topology configured successfully");
    }

    /// <inheritdoc/>
    public async Task<TransportHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var isHealthy = State == TransportState.Connected && _connectionPool != null;

        var health = new TransportHealth
        {
            TransportName = Name,
            Status = isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy,
            State = State,
            StatusMessage = isHealthy ? "RabbitMQ transport is healthy" : $"Transport state: {State}",
            Timestamp = _timeProvider.GetUtcNow(),
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
            var (Total, Active, Idle) = _connectionPool.GetStatistics();
            health.ActiveConnections = Total;
            health.Data["ConnectionsActive"] = Active;
            health.Data["ConnectionsIdle"] = Idle;
        }

        await Task.CompletedTask; // Keep async signature
        return health;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _connectLock.Dispose();
    }

    void IRabbitMqConsumerHost.RemoveConsumer(RabbitMqConsumer consumer)
    {
        // A stale disposal must not remove a replacement registration with the
        // same ID after a disconnect/reconnect race.
        ((ICollection<KeyValuePair<string, RabbitMqConsumer>>)_consumers).Remove(
            new KeyValuePair<string, RabbitMqConsumer>(consumer.ConsumerId, consumer));
    }

    private static Dictionary<string, object?> ToNullableArguments(IEnumerable<KeyValuePair<string, object>> arguments)
    {
        return arguments.ToDictionary(static kvp => kvp.Key, static kvp => (object?)kvp.Value);
    }

    internal async Task PublishMessageAsync(
        IChannel channel,
        string exchange,
        string routingKey,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken,
        bool requireRoute = false)
    {
        if (requireRoute && !_options.UsePublisherConfirms)
            throw new InvalidOperationException("Confirmed queue delivery requires RabbitMQ publisher confirms.");

        if (!_options.UsePublisherConfirms)
        {
            await channel.BasicPublishAsync(exchange, routingKey, false, properties, body, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.PublisherConfirmTimeout);
        try
        {
            await channel.BasicPublishAsync(exchange, routingKey, requireRoute, properties, body, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (PublishReturnException ex) when (requireRoute)
        {
            throw new QueueDeliveryException(routingKey, ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            channel.Dispose();
            throw new TimeoutException($"Publisher confirmation timed out after {_options.PublisherConfirmTimeout}", ex);
        }
        catch (OperationCanceledException)
        {
            channel.Dispose();
            throw;
        }
    }

    private static Dictionary<string, object?> ToNullableArguments(IDictionary<string, object>? arguments)
    {
        return arguments is null ? [] : ToNullableArguments(arguments.AsEnumerable());
    }

    private async Task<RabbitMqChannelPool> GetOrCreateChannelPoolAsync(CancellationToken cancellationToken)
    {
        if (_connectionPool == null)
            throw new InvalidOperationException("Connection pool is not initialized");

        var connection = await _connectionPool.GetConnectionAsync(cancellationToken).ConfigureAwait(false);

        var poolKey = connection.ClientProvidedName ?? "default";

        return _channelPools.GetOrAdd(poolKey, _ => new RabbitMqChannelPool(
            connection,
            maxChannels: _options.MaxChannelsPerConnection,
            channelLifetime: _options.ChannelLifetime,
            _loggerFactory.CreateLogger<RabbitMqChannelPool>(),
            _timeProvider,
            publisherConfirmsEnabled: _options.UsePublisherConfirms));
    }

    private void EnsureConnected()
    {
        if (State != TransportState.Connected)
        {
            throw new InvalidOperationException($"Transport is not connected. Current state: {State}");
        }
    }

    private void ChangeState(TransportState newState, string? reason = null)
    {
        TransportState oldState;
        lock (_stateLock)
        {
            oldState = State;
            State = newState;
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
