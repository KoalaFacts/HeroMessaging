using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;

namespace HeroMessaging.Resilience;

/// <summary>
/// Decorator that adds connection resilience to UnitOfWork operations
/// Handles transient database connection failures with retry and circuit breaker patterns
/// </summary>
public class ConnectionResilienceDecorator(
    IUnitOfWork inner,
    IConnectionResiliencePolicy resiliencePolicy,
    ILogger<ConnectionResilienceDecorator> logger) : IUnitOfWork
{
    private readonly IUnitOfWork _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IConnectionResiliencePolicy _resiliencePolicy = resiliencePolicy ?? throw new ArgumentNullException(nameof(resiliencePolicy));
    private readonly ILogger<ConnectionResilienceDecorator> _logger = logger;

    public IsolationLevel IsolationLevel => _inner.IsolationLevel;
    public bool IsTransactionActive => _inner.IsTransactionActive;

    public IOutboxStorage OutboxStorage => _inner.OutboxStorage;
    public IInboxStorage InboxStorage => _inner.InboxStorage;
    public IQueueStorage QueueStorage => _inner.QueueStorage;
    public IMessageStorage MessageStorage => _inner.MessageStorage;

    public async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.BeginTransactionAsync(isolationLevel, cancellationToken);
        }, "BeginTransaction", cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.CommitAsync(cancellationToken);
        }, "Commit", cancellationToken);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.RollbackAsync(cancellationToken);
        }, "Rollback", cancellationToken);
    }

    public async Task SavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.SavepointAsync(savepointName, cancellationToken);
        }, $"Savepoint-{savepointName}", cancellationToken);
    }

    public async Task RollbackToSavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.RollbackToSavepointAsync(savepointName, cancellationToken);
        }, $"RollbackToSavepoint-{savepointName}", cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _inner.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during UnitOfWork disposal");
            // Don't throw exceptions during disposal
        }
    }
}

/// <summary>
/// Policy interface for connection resilience operations
/// </summary>
public interface IConnectionResiliencePolicy
{
    Task ExecuteAsync(Func<Task> operation, string operationName, CancellationToken cancellationToken = default);
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of connection resilience policy
/// Combines retry logic with circuit breaker pattern
/// </summary>
public class DefaultConnectionResiliencePolicy(
    ConnectionResilienceOptions options,
    ILogger<DefaultConnectionResiliencePolicy> logger,
    ConnectionHealthMonitor? healthMonitor = null) : IConnectionResiliencePolicy
{
    private readonly ConnectionResilienceOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<DefaultConnectionResiliencePolicy> _logger = logger;
    private readonly ConnectionCircuitBreaker _circuitBreaker = new ConnectionCircuitBreaker(options.CircuitBreakerOptions, logger);
    private readonly ConnectionHealthMonitor? _healthMonitor = healthMonitor;

    public async Task ExecuteAsync(Func<Task> operation, string operationName, CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation();
            return 0; // Dummy return value
        }, operationName, cancellationToken);
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(operation, operationName, cancellationToken);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken)
    {
        var retryCount = 0;
        var maxRetries = _options.MaxRetries;
        var startTime = DateTime.UtcNow;

        while (true)
        {
            // Check circuit breaker
            if (!await _circuitBreaker.CanExecuteAsync())
            {
                var duration = DateTime.UtcNow - startTime;
                _healthMonitor?.RecordFailure(operationName,
                    new ConnectionResilienceException("Circuit breaker is open"), duration);
                throw new ConnectionResilienceException($"Circuit breaker is open for operation: {operationName}");
            }

            var operationStartTime = DateTime.UtcNow;
            try
            {
                var result = await operation();
                var operationDuration = DateTime.UtcNow - operationStartTime;

                // Record success in circuit breaker and health monitor
                _circuitBreaker.RecordSuccess();
                _healthMonitor?.RecordSuccess(operationName, operationDuration);

                if (retryCount > 0)
                {
                    _logger.LogInformation("Operation {OperationName} succeeded after {RetryCount} retries",
                        operationName, retryCount);
                }

                return result;
            }
            catch (Exception ex) when (IsTransientException(ex) && retryCount < maxRetries)
            {
                retryCount++;
                var operationDuration = DateTime.UtcNow - operationStartTime;
                var delay = CalculateDelay(retryCount);

                _logger.LogWarning(ex, "Transient error in operation {OperationName}. Retry {RetryCount}/{MaxRetries} after {DelayMs}ms",
                    operationName, retryCount, maxRetries, delay.TotalMilliseconds);

                // Record failure in circuit breaker
                _circuitBreaker.RecordFailure();

                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                // Non-transient exception or max retries exceeded
                var operationDuration = DateTime.UtcNow - operationStartTime;

                _circuitBreaker.RecordFailure();
                _healthMonitor?.RecordFailure(operationName, ex, operationDuration);

                if (retryCount >= maxRetries)
                {
                    _logger.LogError(ex, "Operation {OperationName} failed after {MaxRetries} retries",
                        operationName, maxRetries);
                }
                else
                {
                    _logger.LogError(ex, "Non-transient error in operation {OperationName}", operationName);
                }

                throw;
            }
        }
    }

    private bool IsTransientException(Exception exception)
    {
        return exception switch
        {
            TimeoutException => true,
            TaskCanceledException => false, // Don't retry cancellations
            OperationCanceledException => false,
            DbException dbEx => IsTransientDbException(dbEx),
            InvalidOperationException invalidOpEx when invalidOpEx.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) => true,
            _ => exception.InnerException != null && IsTransientException(exception.InnerException)
        };
    }

    private bool IsTransientDbException(DbException dbException)
    {
        // Common transient error codes for SQL Server
        var transientErrorCodes = new[]
        {
            2,     // Timeout
            20,    // Instance failure
            64,    // Connection failure
            233,   // Connection reset
            10053, // Connection aborted
            10054, // Connection reset by peer
            40197, // Service busy
            40501, // Service busy
            40613, // Database not available
            49918, // Cannot process request
            49919, // Cannot process request
            49920  // Cannot process request
        };

        return transientErrorCodes.Contains(dbException.ErrorCode) ||
               dbException.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               dbException.Message.Contains("connection", StringComparison.OrdinalIgnoreCase);
    }

    private TimeSpan CalculateDelay(int retryCount)
    {
        var baseDelay = _options.BaseRetryDelay.TotalMilliseconds;
        var exponentialDelay = baseDelay * Math.Pow(2, retryCount - 1);
        var jitter = RandomHelper.Instance.NextDouble() * 0.3; // 30% jitter
        var delayWithJitter = exponentialDelay * (1 + jitter);

        return TimeSpan.FromMilliseconds(Math.Min(delayWithJitter, _options.MaxRetryDelay.TotalMilliseconds));
    }
}

/// <summary>
/// Connection-specific circuit breaker
/// </summary>
internal class ConnectionCircuitBreaker(CircuitBreakerOptions options, ILogger logger)
{
    private readonly CircuitBreakerOptions _options = options;
    private readonly ILogger _logger = logger;
    private ConnectionCircuitState _state = ConnectionCircuitState.Closed;
    private DateTime _lastFailureTime;
    private int _failureCount;
    private readonly object _lock = new();

    public async Task<bool> CanExecuteAsync()
    {
        await Task.CompletedTask; // Make it async for future extensibility

        lock (_lock)
        {
            return _state switch
            {
                ConnectionCircuitState.Closed => true,
                ConnectionCircuitState.Open => DateTime.UtcNow - _lastFailureTime >= _options.BreakDuration,
                ConnectionCircuitState.HalfOpen => true,
                _ => false
            };
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            if (_state == ConnectionCircuitState.HalfOpen)
            {
                _state = ConnectionCircuitState.Closed;
                _failureCount = 0;
                _logger.LogInformation("Circuit breaker closed after successful operation");
            }
            else if (_state == ConnectionCircuitState.Closed)
            {
                _failureCount = 0;
            }
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;

            switch (_state)
            {
                case ConnectionCircuitState.Closed when _failureCount >= _options.FailureThreshold:
                    _state = ConnectionCircuitState.Open;
                    _logger.LogWarning("Circuit breaker opened after {FailureCount} failures", _failureCount);
                    break;

                case ConnectionCircuitState.HalfOpen:
                    _state = ConnectionCircuitState.Open;
                    _logger.LogWarning("Circuit breaker reopened after failure in half-open state");
                    break;
            }
        }
    }
}

/// <summary>
/// Configuration options for connection resilience
/// </summary>
public class ConnectionResilienceOptions
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
    public CircuitBreakerOptions CircuitBreakerOptions { get; set; } = new();
}

/// <summary>
/// Circuit breaker specific options
/// </summary>
public class CircuitBreakerOptions
{
    public int FailureThreshold { get; set; } = 5;
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Connection circuit breaker states
/// </summary>
public enum ConnectionCircuitState
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>
/// Exception thrown when connection resilience fails
/// </summary>
public class ConnectionResilienceException : Exception
{
    public ConnectionResilienceException() : base("Connection resilience failed") { }
    public ConnectionResilienceException(string message) : base(message) { }
    public ConnectionResilienceException(string message, Exception innerException) : base(message, innerException) { }
}