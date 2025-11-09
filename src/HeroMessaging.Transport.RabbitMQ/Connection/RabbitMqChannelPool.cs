using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Collections.Concurrent;

namespace HeroMessaging.Transport.RabbitMQ.Connection;

/// <summary>
/// Manages a pool of RabbitMQ channels per connection for optimal resource usage
/// Channels are lightweight and fast to create, but pooling reduces GC pressure
/// </summary>
internal sealed class RabbitMqChannelPool : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly ILogger<RabbitMqChannelPool> _logger;
    private readonly ConcurrentBag<PooledChannel> _channels = new();
    private readonly SemaphoreSlim _createChannelLock = new(1, 1);
    private readonly int _maxChannels;
    private readonly TimeSpan _channelLifetime;
    private readonly TimeProvider _timeProvider;
    private int _channelCount;
    private bool _disposed;

    public RabbitMqChannelPool(
        IConnection connection,
        int maxChannels,
        TimeSpan channelLifetime,
        ILogger<RabbitMqChannelPool> logger,
        TimeProvider timeProvider)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
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

        // Try to get an available channel from the pool
        while (_channels.TryTake(out var pooledChannel))
        {
            if (pooledChannel.IsHealthy && !pooledChannel.IsExpired)
            {
                _logger.LogTrace("Reusing channel {ChannelNumber} from pool", pooledChannel.Channel.ChannelNumber);
                return pooledChannel.Channel;
            }
            else
            {
                // Channel is unhealthy or expired, dispose it
                _logger.LogDebug("Disposing unhealthy/expired channel {ChannelNumber}", pooledChannel.Channel.ChannelNumber);
                pooledChannel.Dispose();
                Interlocked.Decrement(ref _channelCount);
            }
        }

        // Create new channel if under limit
        if (_channelCount < _maxChannels)
        {
            await _createChannelLock.WaitAsync(cancellationToken);
            try
            {
                if (_channelCount < _maxChannels)
                {
                    return await CreateChannelAsync(cancellationToken);
                }
            }
            finally
            {
                _createChannelLock.Release();
            }
        }

        // If pool is full, create a temporary channel (not pooled)
        _logger.LogWarning("Channel pool is full ({MaxChannels}). Creating temporary channel", _maxChannels);
        return await CreateChannelAsync(cancellationToken);
    }

    /// <summary>
    /// Release a channel back to the pool
    /// </summary>
    public void ReleaseChannel(IChannel channel)
    {
        if (_disposed || channel == null || !channel.IsOpen)
        {
            channel?.Dispose();
            return;
        }

        var pooledChannel = new PooledChannel(channel, _channelLifetime, _timeProvider);
        _channels.Add(pooledChannel);

        _logger.LogTrace("Channel {ChannelNumber} returned to pool", channel.ChannelNumber);
    }

    /// <summary>
    /// Create a new channel
    /// </summary>
    private async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken = default)
    {
        if (!_connection.IsOpen)
        {
            throw new InvalidOperationException("Cannot create channel: connection is not open");
        }

        var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        Interlocked.Increment(ref _channelCount);

        _logger.LogDebug("Created new channel {ChannelNumber} (Total: {TotalChannels})",
            channel.ChannelNumber, _channelCount);

        return channel;
    }

    /// <summary>
    /// Execute an operation using a channel from the pool
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<IChannel, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var channel = await AcquireChannelAsync(cancellationToken);
        try
        {
            return await operation(channel);
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
        var channel = await AcquireChannelAsync(cancellationToken);
        try
        {
            await operation(channel);
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
        if (_disposed) return;

        _disposed = true;

        _logger.LogDebug("Disposing channel pool for connection {ConnectionId}", _connection.ClientProvidedName);

        await _createChannelLock.WaitAsync();
        try
        {
            // Dispose all channels
            while (_channels.TryTake(out var pooledChannel))
            {
                pooledChannel.Dispose();
            }

            _logger.LogDebug("Channel pool disposed. Total channels created: {TotalChannels}", _channelCount);
        }
        finally
        {
            _createChannelLock.Release();
            _createChannelLock.Dispose();
        }
    }

    /// <summary>
    /// Represents a channel in the pool with metadata
    /// </summary>
    private sealed class PooledChannel : IDisposable
    {
        private readonly TimeSpan _lifetime;
        private readonly TimeProvider _timeProvider;
        private readonly DateTime _created;
        private bool _disposed;

        public IChannel Channel { get; }
        public bool IsHealthy => Channel.IsOpen && !_disposed;
        public bool IsExpired => (_timeProvider.GetUtcNow().DateTime - _created) > _lifetime;

        public PooledChannel(IChannel channel, TimeSpan lifetime, TimeProvider timeProvider)
        {
            Channel = channel;
            _lifetime = lifetime;
            _timeProvider = timeProvider;
            _created = _timeProvider.GetUtcNow().DateTime;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (Channel.IsOpen)
                {
                    Channel.CloseAsync().GetAwaiter().GetResult();
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
