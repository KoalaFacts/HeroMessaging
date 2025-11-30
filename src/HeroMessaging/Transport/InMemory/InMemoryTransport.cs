using System.Collections.Concurrent;
using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;

namespace HeroMessaging.Transport.InMemory;

/// <summary>
/// In-memory message transport implementation for testing and development
/// High-performance implementation using System.Threading.Channels
/// </summary>
public class InMemoryTransport(
    InMemoryTransportOptions options,
    TimeProvider timeProvider,
    ITransportInstrumentation? instrumentation = null) : IMessageTransport
{
    private static readonly Random _random = Random.Shared;

    // Helper to get Random instance
    private static Random GetRandom() => _random;

    private readonly InMemoryTransportOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ITransportInstrumentation _instrumentation = instrumentation ?? NoOpTransportInstrumentation.Instance;
    private readonly ConcurrentDictionary<string, InMemoryQueue> _queues = new();
    private readonly ConcurrentDictionary<string, InMemoryTopic> _topics = new();
    private readonly ConcurrentDictionary<string, InMemoryConsumer> _consumers = new();
    private TransportState _state = TransportState.Disconnected;
#if NET9_0_OR_GREATER
    private readonly Lock _stateLock = new();
#else
    private readonly object _stateLock = new();
#endif
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc/>
    public string Name => _options.Name;

    /// <inheritdoc/>
    public TransportState State => _state;

    /// <inheritdoc/>
    public event EventHandler<TransportStateChangedEventArgs>? StateChanged;

    /// <inheritdoc/>
    public event EventHandler<TransportErrorEventArgs>? Error;

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_state == TransportState.Connected)
                return;

            // Simulate network delay if configured
            if (_options.SimulateNetworkDelay)
            {
                ChangeState(TransportState.Connecting, "Connecting to in-memory transport");
                await Task.Delay(_options.SimulatedDelayMin, _timeProvider, cancellationToken);
                ChangeState(TransportState.Connected, "Connected to in-memory transport");
            }
            else
            {
                // For in-memory transport, skip intermediate Connecting state since connection is instant
                ChangeState(TransportState.Connected, "Connected to in-memory transport");
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(TransportState.Disconnecting, "Disconnecting from in-memory transport");

        // Stop all consumers and AWAIT their completion
        var stopTasks = _consumers.Values.Select(c => c.StopAsync(cancellationToken)).ToList();
        await Task.WhenAll(stopTasks);

        // Dispose all queues to stop background processing - use async disposal
        var disposeTasks = _queues.Values.Select(q => q.DisposeAsync().AsTask()).ToList();
        await Task.WhenAll(disposeTasks);

        _consumers.Clear();
        _queues.Clear();
        _topics.Clear();

        ChangeState(TransportState.Disconnected, "Disconnected from in-memory transport");
    }

    /// <inheritdoc/>
    public async Task SendAsync(TransportAddress destination, TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Start send activity for distributed tracing
        var activity = _instrumentation.StartSendActivity(envelope, destination.Name, Name);
        var startTime = _timeProvider.GetTimestamp();

        try
        {
            // Inject trace context into envelope headers
            envelope = _instrumentation.InjectTraceContext(envelope, activity);

            _instrumentation.AddEvent(activity, "send.start");

            // Simulate network delay if configured
            if (_options.SimulateNetworkDelay)
            {
                var delay = GetRandom().Next(
                    (int)_options.SimulatedDelayMin.TotalMilliseconds,
                    (int)_options.SimulatedDelayMax.TotalMilliseconds);
                await Task.Delay(TimeSpan.FromMilliseconds(delay), _timeProvider, cancellationToken);
            }

            var queue = _queues.GetOrAdd(destination.Name, name => new InMemoryQueue(name, _options.MaxQueueLength, _options.DropWhenFull));

            if (!await queue.EnqueueAsync(envelope, cancellationToken))
            {
                _instrumentation.RecordOperation(Name, "send", "failure");
                throw new InvalidOperationException($"Queue '{destination.Name}' is full and DropWhenFull is false");
            }

            _instrumentation.AddEvent(activity, "send.complete");

            // Record successful operation
            var durationMs = _timeProvider.GetElapsedTime(startTime).TotalMilliseconds;
            _instrumentation.RecordSendDuration(Name, destination.Name, envelope.MessageType, durationMs);
            _instrumentation.RecordOperation(Name, "send", "success");
        }
        finally
        {
            // Always dispose the activity to prevent resource leaks
            activity?.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task PublishAsync(TransportAddress topic, TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Start publish activity for distributed tracing
        var activity = _instrumentation.StartPublishActivity(envelope, topic.Name, Name);
        var startTime = _timeProvider.GetTimestamp();

        try
        {
            // Inject trace context into envelope headers
            envelope = _instrumentation.InjectTraceContext(envelope, activity);

            _instrumentation.AddEvent(activity, "publish.start");

            // Simulate network delay if configured
            if (_options.SimulateNetworkDelay)
            {
                var delay = GetRandom().Next(
                    (int)_options.SimulatedDelayMin.TotalMilliseconds,
                    (int)_options.SimulatedDelayMax.TotalMilliseconds);
                await Task.Delay(TimeSpan.FromMilliseconds(delay), _timeProvider, cancellationToken);
            }

            var inMemoryTopic = _topics.GetOrAdd(topic.Name, name => new InMemoryTopic(name));

            await inMemoryTopic.PublishAsync(envelope, cancellationToken);

            _instrumentation.AddEvent(activity, "publish.complete");

            // Record successful operation
            var durationMs = _timeProvider.GetElapsedTime(startTime).TotalMilliseconds;
            _instrumentation.RecordSendDuration(Name, topic.Name, envelope.MessageType, durationMs);
            _instrumentation.RecordOperation(Name, "publish", "success");
        }
        finally
        {
            // Always dispose the activity to prevent resource leaks
            activity?.Dispose();
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
        var consumer = new InMemoryConsumer(
            consumerId,
            source,
            handler,
            options,
            this,
            _timeProvider,
            _instrumentation);

        if (!_consumers.TryAdd(consumerId, consumer))
        {
            throw new InvalidOperationException($"Consumer '{consumerId}' already exists");
        }

        // Subscribe to queue or topic
        if (source.Type == TransportAddressType.Queue)
        {
            var queue = _queues.GetOrAdd(source.Name, name => new InMemoryQueue(name, _options.MaxQueueLength, _options.DropWhenFull));
            queue.AddConsumer(consumer);
        }
        else if (source.Type == TransportAddressType.Topic)
        {
            var topic = _topics.GetOrAdd(source.Name, name => new InMemoryTopic(name));
            topic.AddSubscription(consumer);
        }

        if (options.StartImmediately)
        {
            await consumer.StartAsync(cancellationToken);
        }

        return consumer;
    }

    /// <inheritdoc/>
    public Task ConfigureTopologyAsync(TransportTopology topology, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Create queues
        foreach (var queueDef in topology.Queues)
        {
            _queues.GetOrAdd(queueDef.Name, name => new InMemoryQueue(name, _options.MaxQueueLength, _options.DropWhenFull));
        }

        // Create topics
        foreach (var topicDef in topology.Topics)
        {
            _topics.GetOrAdd(topicDef.Name, name => new InMemoryTopic(name));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<TransportHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var health = new TransportHealth
        {
            TransportName = Name,
            Status = _state == TransportState.Connected ? HealthStatus.Healthy : HealthStatus.Unhealthy,
            State = _state,
            StatusMessage = _state == TransportState.Connected ? "In-memory transport is healthy" : $"Transport state: {_state}",
            Timestamp = _timeProvider.GetUtcNow(),
            Duration = TimeSpan.Zero,
            ActiveConnections = 1,
            ActiveConsumers = _consumers.Count,
            PendingMessages = _queues.Values.Sum(q => q.Depth) + _topics.Values.Sum(t => t.PendingMessages),
            Data = new Dictionary<string, object>
            {
                ["QueueCount"] = _queues.Count,
                ["TopicCount"] = _topics.Count,
                ["ConsumerCount"] = _consumers.Count
            }
        };

        return Task.FromResult(health);
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
