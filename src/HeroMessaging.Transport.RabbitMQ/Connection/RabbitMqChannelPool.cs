using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace HeroMessaging.Transport.RabbitMQ.Connection;

/// <summary>
/// Manages a pool of RabbitMQ channels per connection for optimal resource usage
/// Channels are lightweight and fast to create, but pooling reduces GC pressure
/// </summary>
internal sealed class RabbitMqChannelPool : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly ILogger<RabbitMqChannelPool> _logger;
    private readonly ConcurrentBag<PooledChannel> _channels = [];
    // Weak keys prevent an abandoned externally disposed overflow channel from being retained by the pool.
    private readonly ConditionalWeakTable<IChannel, TemporaryChannelMarker> _temporaryChannels = [];
    // Weak ownership keys avoid retaining channels that a consumer abandoned or disposed externally.
    private readonly ConditionalWeakTable<IChannel, RetainedChannelMarker> _retainedChannels = [];
    private readonly object _returnLock = new();
    private readonly SemaphoreSlim _createChannelLock = new(1, 1);
    private readonly int _maxChannels;
    private readonly TimeSpan _channelLifetime;
    private readonly TimeProvider _timeProvider;
    private readonly bool _publisherConfirmsEnabled;
    private int _channelCount;
    private volatile bool _disposed;

    public RabbitMqChannelPool(
        IConnection connection,
        int maxChannels,
        TimeSpan channelLifetime,
        ILogger<RabbitMqChannelPool> logger,
        TimeProvider timeProvider,
        bool publisherConfirmsEnabled = false)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _publisherConfirmsEnabled = publisherConfirmsEnabled;
        _maxChannels = maxChannels;
        _channelLifetime = channelLifetime;

        _logger.LogDebug("Channel pool created for connection {ConnectionId}, MaxChannels: {MaxChannels}",
            _connection.ClientProvidedName, _maxChannels);
    }

    /// <summary>
    /// Acquire a channel from the pool
    /// </summary>
    public async Task<IChannel> AcquireChannelAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Only one outstanding borrower may use a retained channel. A stale or
        // duplicate bag entry must never hand out the same channel twice.
        while (_channels.TryTake(out var pooledChannel))
        {
            if (!_retainedChannels.TryGetValue(pooledChannel.Channel, out var retained) ||
                !retained.TryBorrow())
                continue;

            if (pooledChannel.IsHealthy && !pooledChannel.IsExpired)
            {
                _logger.LogTrace("Reusing channel {ChannelNumber} from pool", pooledChannel.Channel.ChannelNumber);
                return pooledChannel.Channel;
            }

            _logger.LogDebug("Disposing unhealthy/expired channel {ChannelNumber}", pooledChannel.Channel.ChannelNumber);
            if (TryRetirePooledChannel(pooledChannel.Channel))
                pooledChannel.Dispose();
        }

        // Create new channel if under limit
        if (_channelCount < _maxChannels)
        {
            await _createChannelLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_channelCount < _maxChannels)
                {
                    return await CreateChannelAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _createChannelLock.Release();
            }
        }

        // If pool is full, create a temporary channel (not pooled)
        _logger.LogWarning("Channel pool is full ({MaxChannels}). Creating temporary channel", _maxChannels);
        return await CreateChannelAsync(cancellationToken, pooled: false).ConfigureAwait(false);
    }

    /// <summary>
    /// Release a channel back to the pool
    /// </summary>
    public void ReleaseChannel(IChannel channel)
    {
        if (channel == null)
            return;

        // Temporary overflow channels never become reusable pool capacity.
        if (_temporaryChannels.TryGetValue(channel, out var temporary))
        {
            if (temporary.TryMarkReleased())
                channel.Dispose();
            return;
        }

        if (!_retainedChannels.TryGetValue(channel, out var retained))
        {
            // Unknown channels must never be adopted into the reusable pool.
            channel.Dispose();
            return;
        }

        lock (_returnLock)
        {
            if (_disposed || !channel.IsOpen)
            {
                if (TryRetirePooledChannel(channel))
                    channel.Dispose();
                return;
            }

            // Idempotent returns prevent duplicate bag entries and concurrent
            // use of one IChannel by multiple callers.
            if (!retained.TryReturn())
                return;

            _channels.Add(new PooledChannel(channel, _channelLifetime, _timeProvider));
        }

        _logger.LogTrace("Channel {ChannelNumber} returned to pool", channel.ChannelNumber);
    }

    private bool TryRetirePooledChannel(IChannel channel)
    {
        if (!_retainedChannels.TryGetValue(channel, out var retained) || !retained.TryRetire())
            return false;

        Interlocked.Decrement(ref _channelCount);
        return true;
    }

    /// <summary>
    /// Create a new channel
    /// </summary>
    private async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default, bool pooled = true)
    {
        if (!_connection.IsOpen)
        {
            throw new InvalidOperationException("Cannot create channel: connection is not open");
        }

        var channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: _publisherConfirmsEnabled,
                publisherConfirmationTrackingEnabled: _publisherConfirmsEnabled),
            cancellationToken).ConfigureAwait(false);
        if (pooled)
        {
            _retainedChannels.Add(channel, new RetainedChannelMarker());
            Interlocked.Increment(ref _channelCount);
        }
        else
        {
            _temporaryChannels.Add(channel, new TemporaryChannelMarker());
        }

        _logger.LogDebug("Created {ChannelKind} channel {ChannelNumber} (Pooled count: {PooledChannels})",
            pooled ? "pooled" : "temporary", channel.ChannelNumber, _channelCount);

        return channel;
    }

    /// <summary>
    /// Execute an operation using a channel from the pool
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<IChannel, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var channel = await AcquireChannelAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation(channel).ConfigureAwait(false);
        }
        finally
        {
            ReleaseChannel(channel);
        }
    }

    /// <summary>
    /// Execute an operation using a channel from the pool
    /// </summary>
    public async Task ExecuteAsync(
        Func<IChannel, Task> operation,
        CancellationToken cancellationToken = default)
    {
        var channel = await AcquireChannelAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await operation(channel).ConfigureAwait(false);
        }
        finally
        {
            ReleaseChannel(channel);
        }
    }

    /// <summary>
    /// Get current pool statistics
    /// </summary>
    public (int Total, int Available) GetStatistics()
    {
        var available = _channels.Count(c => c.IsHealthy && !c.IsExpired);
        return (_channelCount, available);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RabbitMqChannelPool));
    }

    public async ValueTask DisposeAsync()
    {
        // Coordinate returns with the final bag drain: no return can enqueue
        // a channel after disposal has begun.
        lock (_returnLock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _logger.LogDebug("Disposing channel pool for connection {ConnectionId}", _connection.ClientProvidedName);

        await _createChannelLock.WaitAsync().ConfigureAwait(false);
        try
        {
            while (_channels.TryTake(out var pooledChannel))
            {
                if (TryRetirePooledChannel(pooledChannel.Channel))
                    pooledChannel.Dispose();
            }

            _logger.LogDebug("Channel pool disposed. Remaining borrowed channels: {TotalChannels}", _channelCount);
        }
        finally
        {
            _createChannelLock.Release();
            _createChannelLock.Dispose();
        }
    }

    private sealed class RetainedChannelMarker
    {
        // 0: borrowed, 1: available, 2: retired. A retired channel never re-enters the bag.
        private int _state;

        public bool TryReturn() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

        public bool TryBorrow() => Interlocked.CompareExchange(ref _state, 0, 1) == 1;

        public bool TryRetire() => Interlocked.Exchange(ref _state, 2) != 2;
    }

    private sealed class TemporaryChannelMarker
    {
        private int _released;

        public bool TryMarkReleased() => Interlocked.Exchange(ref _released, 1) == 0;
    }

    /// <summary>
    /// Represents a channel in the pool with metadata
    /// </summary>
    private sealed class PooledChannel : IAsyncDisposable, IDisposable
    {
        private readonly TimeSpan _lifetime;
        private readonly TimeProvider _timeProvider;
        private readonly DateTimeOffset _created;
        private bool _disposed;

        public IChannel Channel { get; }
        public bool IsHealthy => Channel.IsOpen && !_disposed;
        public bool IsExpired => (_timeProvider.GetUtcNow() - _created) > _lifetime;

        public PooledChannel(IChannel channel, TimeSpan lifetime, TimeProvider timeProvider)
        {
            Channel = channel;
            _lifetime = lifetime;
            _timeProvider = timeProvider;
            _created = _timeProvider.GetUtcNow();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (Channel.IsOpen)
                {
                    await Channel.CloseAsync().ConfigureAwait(false);
                }
                Channel.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                // Non-blocking disposal: schedule async close on thread pool
                if (Channel.IsOpen)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Channel.CloseAsync().ConfigureAwait(false);
                        }
                        catch
                        {
                            // Ignore errors during async close
                        }
                    });
                }
                Channel.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }
}
