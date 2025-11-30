using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;

namespace HeroMessaging.Transport.RabbitMQ.Connection;

/// <summary>
/// Manages a pool of RabbitMQ connections for optimal resource usage and performance
/// </summary>
internal sealed class RabbitMqConnectionPool : IAsyncDisposable
{
    private const int MaxGetConnectionRetries = 50; // Prevent stack overflow from recursion

    private readonly RabbitMqTransportOptions _options;
    private readonly ILogger<RabbitMqConnectionPool> _logger;
    private readonly IConnectionFactory _connectionFactory;
    private readonly ConcurrentDictionary<string, PooledConnection> _connections = new();
    private readonly SemaphoreSlim _createConnectionLock = new(1, 1);
    private readonly Timer _healthCheckTimer;
    private readonly TimeProvider _timeProvider;
    private int _connectionCount;
    private bool _disposed;

    public RabbitMqConnectionPool(
        RabbitMqTransportOptions options,
        ILogger<RabbitMqConnectionPool> logger,
        TimeProvider timeProvider)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        // Create connection factory
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            VirtualHost = _options.VirtualHost,
            UserName = _options.UserName,
            Password = _options.Password,
            RequestedHeartbeat = _options.Heartbeat,
            AutomaticRecoveryEnabled = _options.AutoReconnect,
            NetworkRecoveryInterval = _options.ReconnectionPolicy.InitialDelay,
            TopologyRecoveryEnabled = true
        };

        if (_options.UseSsl)
        {
            factory.Ssl = new SslOption
            {
                Enabled = true,
                ServerName = _options.Host
            };
        }

        _connectionFactory = factory;

        // Start health check timer (every 30 seconds)
        _healthCheckTimer = new Timer(
            callback: _ => PerformHealthCheck(),
            state: null,
            dueTime: TimeSpan.FromSeconds(30),
            period: TimeSpan.FromSeconds(30));

        _logger.LogInformation(
            "RabbitMQ connection pool initialized. Host: {Host}, Port: {Port}, VirtualHost: {VirtualHost}, PoolSize: {MinSize}-{MaxSize}",
            _options.Host, _options.Port, _options.VirtualHost, _options.MinPoolSize, _options.MaxPoolSize);
    }

    /// <summary>
    /// Get an available connection from the pool
    /// </summary>
    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        for (int retryCount = 0; retryCount < MaxGetConnectionRetries; retryCount++)
        {
            // Try to find an existing healthy connection
            foreach (var kvp in _connections)
            {
                var pooledConnection = kvp.Value;
                if (pooledConnection.IsHealthy && pooledConnection.TryAcquire())
                {
                    _logger.LogTrace("Reusing existing connection {ConnectionId}", pooledConnection.Connection.ClientProvidedName);
                    return pooledConnection.Connection;
                }
            }

            // Create new connection if under max pool size
            if (_connectionCount < _options.MaxPoolSize)
            {
                await _createConnectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    // Double-check after acquiring lock
                    if (_connectionCount < _options.MaxPoolSize)
                    {
                        return await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _createConnectionLock.Release();
                }
            }

            // Wait and retry if pool is full (iterative instead of recursive)
            if (retryCount == 0)
            {
                _logger.LogWarning("Connection pool is full ({MaxSize} connections). Waiting for available connection...", _options.MaxPoolSize);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100), _timeProvider, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"Unable to acquire connection from pool after {MaxGetConnectionRetries} attempts. Pool may be exhausted.");
    }

    /// <summary>
    /// Create a new connection and add it to the pool
    /// </summary>
    private async Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionName = $"HeroMessaging-{_options.Name}-{Interlocked.Increment(ref _connectionCount)}";

        _logger.LogInformation("Creating new RabbitMQ connection: {ConnectionName}", connectionName);

        try
        {
            // Create connection with timeout
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.ConnectionTimeout);

            IConnection connection;
            try
            {
                connection = await _connectionFactory.CreateConnectionAsync(connectionName, cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Connection to RabbitMQ timed out after {_options.ConnectionTimeout}");
            }

            // Set up event handlers
            connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;
            connection.ConnectionBlockedAsync += OnConnectionBlockedAsync;
            connection.ConnectionUnblockedAsync += OnConnectionUnblockedAsync;
            connection.CallbackExceptionAsync += OnCallbackExceptionAsync;

            var pooledConnection = new PooledConnection(connection, _options.ConnectionIdleTimeout, _timeProvider);
            _connections.TryAdd(connectionName, pooledConnection);

            _logger.LogInformation(
                "RabbitMQ connection created successfully: {ConnectionName}, Endpoint: {Endpoint}",
                connectionName, connection.Endpoint);

            return connection;
        }
        catch (Exception ex)
        {
            Interlocked.Decrement(ref _connectionCount);
            _logger.LogError(ex, "Failed to create RabbitMQ connection: {ConnectionName}", connectionName);
            throw;
        }
    }

    /// <summary>
    /// Release a connection back to the pool
    /// </summary>
    public void ReleaseConnection(IConnection connection)
    {
        foreach (var kvp in _connections)
        {
            if (kvp.Value.Connection == connection)
            {
                kvp.Value.Release();
                _logger.LogTrace("Released connection {ConnectionId}", connection.ClientProvidedName);
                return;
            }
        }
    }

    /// <summary>
    /// Perform periodic health check on all connections
    /// </summary>
    private void PerformHealthCheck()
    {
        if (_disposed) return;

        _logger.LogTrace("Performing health check on {Count} connections", _connections.Count);

        foreach (var kvp in _connections)
        {
            var connectionKey = kvp.Key;
            var pooledConnection = kvp.Value;

            if (!pooledConnection.IsHealthy)
            {
                _logger.LogWarning(
                    "Removing unhealthy connection: {ConnectionId}",
                    pooledConnection.Connection.ClientProvidedName);

                // Remove from pool using ConcurrentDictionary (proper removal support)
                if (_connections.TryRemove(connectionKey, out var removed))
                {
                    removed.Dispose();
                    Interlocked.Decrement(ref _connectionCount);
                }
            }
            else if (pooledConnection.IsIdle)
            {
                _logger.LogDebug(
                    "Connection {ConnectionId} has been idle for {IdleTime}",
                    pooledConnection.Connection.ClientProvidedName,
                    _timeProvider.GetUtcNow() - pooledConnection.LastUsed);

                // Keep at least MinPoolSize connections
                if (_connectionCount > _options.MinPoolSize)
                {
                    _logger.LogInformation(
                        "Closing idle connection: {ConnectionId}",
                        pooledConnection.Connection.ClientProvidedName);

                    if (_connections.TryRemove(connectionKey, out var removed))
                    {
                        removed.Dispose();
                        Interlocked.Decrement(ref _connectionCount);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Get current pool statistics
    /// </summary>
    public (int Total, int Active, int Idle) GetStatistics()
    {
        var total = _connectionCount;
        var active = _connections.Values.Count(c => c.IsHealthy && !c.IsIdle);
        var idle = _connections.Values.Count(c => c.IsHealthy && c.IsIdle);

        return (total, active, idle);
    }

    #region Event Handlers

    private Task OnConnectionShutdownAsync(object? sender, ShutdownEventArgs e)
    {
        var connection = sender as IConnection;
        _logger.LogWarning(
            "RabbitMQ connection shutdown: {ConnectionId}, ReplyCode: {ReplyCode}, ReplyText: {ReplyText}",
            connection?.ClientProvidedName, e.ReplyCode, e.ReplyText);
        return Task.CompletedTask;
    }

    private Task OnConnectionBlockedAsync(object? sender, ConnectionBlockedEventArgs e)
    {
        var connection = sender as IConnection;
        _logger.LogWarning(
            "RabbitMQ connection blocked: {ConnectionId}, Reason: {Reason}",
            connection?.ClientProvidedName, e.Reason);
        return Task.CompletedTask;
    }

    private Task OnConnectionUnblockedAsync(object? sender, AsyncEventArgs e)
    {
        var connection = sender as IConnection;
        _logger.LogInformation(
            "RabbitMQ connection unblocked: {ConnectionId}",
            connection?.ClientProvidedName);
        return Task.CompletedTask;
    }

    private Task OnCallbackExceptionAsync(object? sender, CallbackExceptionEventArgs e)
    {
        _logger.LogError(
            e.Exception,
            "RabbitMQ callback exception: {Detail}",
            e.Detail);
        return Task.CompletedTask;
    }

    #endregion

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RabbitMqConnectionPool));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;

        _logger.LogInformation("Disposing RabbitMQ connection pool...");

        await _healthCheckTimer.DisposeAsync().ConfigureAwait(false);
        _createConnectionLock.Dispose();

        // Close all connections asynchronously
        var disposeTasks = _connections.Values.Select(c => c.DisposeAsync().AsTask()).ToList();
        await Task.WhenAll(disposeTasks).ConfigureAwait(false);
        _connections.Clear();

        _logger.LogInformation("RabbitMQ connection pool disposed");
    }

    /// <summary>
    /// Represents a connection in the pool with metadata
    /// </summary>
    private sealed class PooledConnection : IAsyncDisposable, IDisposable
    {
        private readonly TimeSpan _idleTimeout;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger? _logger;
        private int _inUseCount;
        private bool _disposed;

        public IConnection Connection { get; }
        public DateTimeOffset LastUsed { get; private set; }
        public bool IsHealthy => Connection.IsOpen && !_disposed;
        public bool IsIdle => (_timeProvider.GetUtcNow() - LastUsed) > _idleTimeout && _inUseCount == 0;

        public PooledConnection(IConnection connection, TimeSpan idleTimeout, TimeProvider timeProvider, ILogger? logger = null)
        {
            Connection = connection;
            _idleTimeout = idleTimeout;
            _timeProvider = timeProvider;
            _logger = logger;
            LastUsed = _timeProvider.GetUtcNow();
        }

        public bool TryAcquire()
        {
            if (!IsHealthy || _disposed) return false;

            Interlocked.Increment(ref _inUseCount);
            LastUsed = _timeProvider.GetUtcNow();
            return true;
        }

        public void Release()
        {
            Interlocked.Decrement(ref _inUseCount);
            LastUsed = _timeProvider.GetUtcNow();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (Connection.IsOpen)
                {
                    await Connection.CloseAsync().ConfigureAwait(false);
                }
                Connection.Dispose();
            }
            catch (Exception ex)
            {
                // Log disposal errors instead of silently ignoring
                _logger?.LogDebug(ex, "Error disposing RabbitMQ connection: {ConnectionId}", Connection.ClientProvidedName);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                // Non-blocking disposal: schedule async close on thread pool to prevent thread starvation
                if (Connection.IsOpen)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Connection.CloseAsync().ConfigureAwait(false);
                        }
                        catch
                        {
                            // Ignore errors during async close
                        }
                    });
                }
                Connection.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Error disposing RabbitMQ connection: {ConnectionId}", Connection.ClientProvidedName);
            }
        }
    }
}
