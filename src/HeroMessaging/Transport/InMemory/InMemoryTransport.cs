using HeroMessaging.Abstractions.Transport;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace HeroMessaging.Transport.InMemory;

/// <summary>
/// In-memory message transport implementation for testing and development
/// High-performance implementation using System.Threading.Channels
/// </summary>
public class InMemoryTransport : IMessageTransport
{
    private static readonly Random _random = new Random();
    private readonly InMemoryTransportOptions _options;
    private readonly ConcurrentDictionary<string, InMemoryQueue> _queues = new();
    private readonly ConcurrentDictionary<string, InMemoryTopic> _topics = new();
    private readonly ConcurrentDictionary<string, InMemoryConsumer> _consumers = new();
    private TransportState _state = TransportState.Disconnected;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    /// <inheritdoc/>
    public string Name => _options.Name;

    /// <inheritdoc/>
    public TransportState State => _state;

    /// <inheritdoc/>
    public event EventHandler<TransportStateChangedEventArgs>? StateChanged;

    /// <inheritdoc/>
    public event EventHandler<TransportErrorEventArgs>? Error;

    public InMemoryTransport(InMemoryTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_state == TransportState.Connected)
                return;

            ChangeState(TransportState.Connecting, "Connecting to in-memory transport");

            // Simulate network delay if configured
            if (_options.SimulateNetworkDelay)
            {
                await Task.Delay(_options.SimulatedDelayMin, cancellationToken);
            }

            ChangeState(TransportState.Connected, "Connected to in-memory transport");
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <inheritdoc/>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ChangeState(TransportState.Disconnecting, "Disconnecting from in-memory transport");

        // Stop all consumers
        foreach (var consumer in _consumers.Values)
        {
            _ = consumer.StopAsync(cancellationToken);
        }

        _consumers.Clear();
        _queues.Clear();
        _topics.Clear();

        ChangeState(TransportState.Disconnected, "Disconnected from in-memory transport");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task SendAsync(TransportAddress destination, TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Simulate network delay if configured
        if (_options.SimulateNetworkDelay)
        {
            var delay = _random.Next(
                (int)_options.SimulatedDelayMin.TotalMilliseconds,
                (int)_options.SimulatedDelayMax.TotalMilliseconds);
            await Task.Delay(delay, cancellationToken);
        }

        var queue = _queues.GetOrAdd(destination.Name, _ => new InMemoryQueue(_options.MaxQueueLength, _options.DropWhenFull));

        if (!await queue.EnqueueAsync(envelope, cancellationToken))
        {
            OnError(new InvalidOperationException($"Queue '{destination.Name}' is full and DropWhenFull is false"));
        }
    }

    /// <inheritdoc/>
    public async Task PublishAsync(TransportAddress topic, TransportEnvelope envelope, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Simulate network delay if configured
        if (_options.SimulateNetworkDelay)
        {
            var delay = _random.Next(
                (int)_options.SimulatedDelayMin.TotalMilliseconds,
                (int)_options.SimulatedDelayMax.TotalMilliseconds);
            await Task.Delay(delay, cancellationToken);
        }

        var inMemoryTopic = _topics.GetOrAdd(topic.Name, _ => new InMemoryTopic());

        await inMemoryTopic.PublishAsync(envelope, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ITransportConsumer> SubscribeAsync(
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
            this);

        if (!_consumers.TryAdd(consumerId, consumer))
        {
            throw new InvalidOperationException($"Consumer '{consumerId}' already exists");
        }

        // Subscribe to queue or topic
        if (source.Type == TransportAddressType.Queue)
        {
            var queue = _queues.GetOrAdd(source.Name, _ => new InMemoryQueue(_options.MaxQueueLength, _options.DropWhenFull));
            queue.AddConsumer(consumer);
        }
        else if (source.Type == TransportAddressType.Topic)
        {
            var topic = _topics.GetOrAdd(source.Name, _ => new InMemoryTopic());
            topic.AddSubscription(consumer);
        }

        if (options.StartImmediately)
        {
            _ = consumer.StartAsync(cancellationToken);
        }

        return Task.FromResult<ITransportConsumer>(consumer);
    }

    /// <inheritdoc/>
    public Task ConfigureTopologyAsync(TransportTopology topology, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Create queues
        foreach (var queueDef in topology.Queues)
        {
            _queues.GetOrAdd(queueDef.Name, _ => new InMemoryQueue(_options.MaxQueueLength, _options.DropWhenFull));
        }

        // Create topics
        foreach (var topicDef in topology.Topics)
        {
            _topics.GetOrAdd(topicDef.Name, _ => new InMemoryTopic());
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
            Timestamp = DateTime.UtcNow,
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
