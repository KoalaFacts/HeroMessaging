using System.Data;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Utilities;
using Microsoft.Extensions.Logging;

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
    /// <summary>
    /// Gets isolation level.
    /// </summary>

    public IsolationLevel IsolationLevel => _inner.IsolationLevel;
    /// <summary>
    /// Gets is transaction active.
    /// </summary>
    public bool IsTransactionActive => _inner.IsTransactionActive;
    /// <summary>
    /// Gets outbox storage.
    /// </summary>

    public IOutboxStorage OutboxStorage => _inner.OutboxStorage;
    /// <summary>
    /// Gets inbox storage.
    /// </summary>
    public IInboxStorage InboxStorage => _inner.InboxStorage;
    /// <summary>
    /// Gets queue storage.
    /// </summary>
    public IQueueStorage QueueStorage => _inner.QueueStorage;
    /// <summary>
    /// Gets message storage.
    /// </summary>
    public IMessageStorage MessageStorage => _inner.MessageStorage;
    /// <summary>
    /// Executes begin transaction async.
    /// </summary>

    public async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.BeginTransactionAsync(isolationLevel, cancellationToken);
        }, "BeginTransaction", cancellationToken);
    }
    /// <summary>
    /// Executes commit async.
    /// </summary>

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.CommitAsync(cancellationToken);
        }, "Commit", cancellationToken);
    }
    /// <summary>
    /// Executes rollback async.
    /// </summary>

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.RollbackAsync(cancellationToken);
        }, "Rollback", cancellationToken);
    }
    /// <summary>
    /// Executes savepoint async.
    /// </summary>

    public async Task SavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.SavepointAsync(savepointName, cancellationToken);
        }, $"Savepoint-{savepointName}", cancellationToken);
    }
    /// <summary>
    /// Executes rollback to savepoint async.
    /// </summary>

    public async Task RollbackToSavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.RollbackToSavepointAsync(savepointName, cancellationToken);
        }, $"RollbackToSavepoint-{savepointName}", cancellationToken);
    }
    /// <summary>
    /// Executes dispose async.
    /// </summary>

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
    /// <summary>
    /// Executes execute async.
    /// </summary>
    Task ExecuteAsync(Func<Task> operation, string operationName, CancellationToken cancellationToken = default);
    /// <summary>
    /// Executes execute async.
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of connection resilience policy
/// Combines retry logic with circuit breaker pattern
/// </summary>
public class DefaultConnectionResiliencePolicy(
    ConnectionResilienceOptions options,
    ILogger<DefaultConnectionResiliencePolicy> logger,
    TimeProvider timeProvider,
    ConnectionHealthMonitor? healthMonitor = null) : IConnectionResiliencePolicy
{
    /// <summary>
    /// Represents options.
    /// </summary>
    private readonly ConnectionResilienceOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<DefaultConnectionResiliencePolicy> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    /// <summary>
    /// Represents circuit breaker.
    /// </summary>
    private readonly ConnectionCircuitBreaker _circuitBreaker = new(options.CircuitBreakerOptions, logger, timeProvider);
    private readonly ConnectionHealthMonitor? _healthMonitor = healthMonitor;
    /// <summary>
    /// Executes execute async.
    /// </summary>

    public async Task ExecuteAsync(Func<Task> operation, string operationName, CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation();
            return 0; // Dummy return value
        }, operationName, cancellationToken);
    }
    /// <summary>
    /// Executes execute async.
    /// </summary>

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(operation, operationName, cancellationToken);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken)
    {
        var retryCount = 0;
        var maxRetries = _options.MaxRetries;
        var startTime = _timeProvider.GetUtcNow();

        while (true)
        {
            // Check circuit breaker
            if (!await _circuitBreaker.CanExecuteAsync())
            {
                var duration = _timeProvider.GetUtcNow() - startTime;
                _healthMonitor?.RecordFailure(operationName,
                    new ConnectionResilienceException("Circuit breaker is open"), duration);
                throw new ConnectionResilienceException($"Circuit breaker is open for operation: {operationName}");
            }

            var operationStartTime = _timeProvider.GetUtcNow();
            try
            {
                var result = await operation();
                var operationDuration = _timeProvider.GetUtcNow() - operationStartTime;

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
                var operationDuration = _timeProvider.GetUtcNow() - operationStartTime;
                var delay = CalculateDelay(retryCount);

                _logger.LogWarning(ex, "Transient error in operation {OperationName}. Retry {RetryCount}/{MaxRetries} after {DelayMs}ms",
                    operationName, retryCount, maxRetries, delay.TotalMilliseconds);

                await Task.Delay(delay, _timeProvider, cancellationToken);
            }
            catch (Exception ex)
            {
                // Non-transient exception or max retries exceeded
                var operationDuration = _timeProvider.GetUtcNow() - operationStartTime;

                // Record circuit breaker failure only once per operation, not per retry
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

    private static bool IsTransientException(Exception exception) =>
        ErrorClassifier.IsTransient(exception, checkInnerException: true, treatCancellationAsTransient: false);

    private TimeSpan CalculateDelay(int retryCount) =>
        RetryDelayCalculator.Calculate(retryCount, _options.BaseRetryDelay, _options.MaxRetryDelay,
            RetryDelayCalculator.DefaultJitterFactor, useZeroBasedAttempt: false);
}

/// <summary>
/// Connection-specific circuit breaker
/// </summary>
internal class ConnectionCircuitBreaker(CircuitBreakerOptions options, ILogger logger, TimeProvider timeProvider)
{
    private readonly CircuitBreakerOptions _options = options;
    private readonly ILogger _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider;
    private ConnectionCircuitState _state = ConnectionCircuitState.Closed;
    private DateTimeOffset _lastFailureTime;
    private int _failureCount;
#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif

    public async Task<bool> CanExecuteAsync()
    {
        await Task.CompletedTask; // Make it async for future extensibility

        lock (_lock)
        {
            switch (_state)
            {
                case ConnectionCircuitState.Closed:
                    return true;

                case ConnectionCircuitState.Open:
                    if (_timeProvider.GetUtcNow() - _lastFailureTime >= _options.BreakDuration)
                    {
                        // Transition to half-open to allow retry
                        _state = ConnectionCircuitState.HalfOpen;
                        _logger.LogInformation("Circuit breaker transitioning to half-open state");
                        return true;
                    }
                    return false;

                case ConnectionCircuitState.HalfOpen:
                    return true;

                default:
                    return false;
            }
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
    /// <summary>
    /// Executes record failure.
    /// </summary>

    public void RecordFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            _lastFailureTime = _timeProvider.GetUtcNow();

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
                case ConnectionCircuitState.Closed:
                    break;
                case ConnectionCircuitState.Open:
                    break;
                default:
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
    /// <summary>
    /// Gets or sets max retries.
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    /// <summary>
    /// Gets or sets base retry delay.
    /// </summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    /// <summary>
    /// Gets or sets max retry delay.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>
    /// Gets or sets circuit breaker options.
    /// </summary>
    public CircuitBreakerOptions CircuitBreakerOptions { get; set; } = new();
}

/// <summary>
/// Circuit breaker specific options
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>
    /// Gets or sets failure threshold.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;
    /// <summary>
    /// Gets or sets break duration.
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Connection circuit breaker states
/// </summary>
public enum ConnectionCircuitState
{
    /// <summary>
    /// Specifies closed.
    /// </summary>
    Closed,
    /// <summary>
    /// Specifies open.
    /// </summary>
    Open,
    /// <summary>
    /// Specifies half open.
    /// </summary>
    HalfOpen
}

/// <summary>
/// Exception thrown when connection resilience fails
/// </summary>
public class ConnectionResilienceException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionResilienceException"/> class.
    /// </summary>
    public ConnectionResilienceException() : base("Connection resilience failed") { }
    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionResilienceException"/> class.
    /// </summary>
    public ConnectionResilienceException(string message) : base(message) { }
    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionResilienceException"/> class.
    /// </summary>
    public ConnectionResilienceException(string message, Exception innerException) : base(message, innerException) { }
}
