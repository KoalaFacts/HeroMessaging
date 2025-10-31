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

    /// <summary>
    /// Gets the isolation level of the current transaction
    /// </summary>
    public IsolationLevel IsolationLevel => _inner.IsolationLevel;

    /// <summary>
    /// Gets a value indicating whether a transaction is currently active
    /// </summary>
    public bool IsTransactionActive => _inner.IsTransactionActive;

    /// <summary>
    /// Gets the outbox storage for transactional messaging
    /// </summary>
    public IOutboxStorage OutboxStorage => _inner.OutboxStorage;

    /// <summary>
    /// Gets the inbox storage for message deduplication
    /// </summary>
    public IInboxStorage InboxStorage => _inner.InboxStorage;

    /// <summary>
    /// Gets the queue storage for message processing
    /// </summary>
    public IQueueStorage QueueStorage => _inner.QueueStorage;

    /// <summary>
    /// Gets the message storage for persistent message tracking
    /// </summary>
    public IMessageStorage MessageStorage => _inner.MessageStorage;

    /// <summary>
    /// Begins a new database transaction with the specified isolation level
    /// </summary>
    /// <param name="isolationLevel">The isolation level for the transaction</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method applies connection resilience with retry logic and circuit breaker protection
    /// to handle transient database connection failures during transaction initialization
    /// </remarks>
    public async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.BeginTransactionAsync(isolationLevel, cancellationToken);
        }, "BeginTransaction", cancellationToken);
    }

    /// <summary>
    /// Commits the current transaction
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method applies connection resilience with retry logic and circuit breaker protection
    /// to handle transient database connection failures during transaction commit
    /// </remarks>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.CommitAsync(cancellationToken);
        }, "Commit", cancellationToken);
    }

    /// <summary>
    /// Rolls back the current transaction
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method applies connection resilience with retry logic and circuit breaker protection
    /// to handle transient database connection failures during transaction rollback
    /// </remarks>
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.RollbackAsync(cancellationToken);
        }, "Rollback", cancellationToken);
    }

    /// <summary>
    /// Creates a savepoint within the current transaction
    /// </summary>
    /// <param name="savepointName">The name of the savepoint to create</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method applies connection resilience with retry logic and circuit breaker protection
    /// to handle transient database connection failures during savepoint creation
    /// </remarks>
    public async Task SavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.SavepointAsync(savepointName, cancellationToken);
        }, $"Savepoint-{savepointName}", cancellationToken);
    }

    /// <summary>
    /// Rolls back the transaction to a previously created savepoint
    /// </summary>
    /// <param name="savepointName">The name of the savepoint to roll back to</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method applies connection resilience with retry logic and circuit breaker protection
    /// to handle transient database connection failures during savepoint rollback
    /// </remarks>
    public async Task RollbackToSavepointAsync(string savepointName, CancellationToken cancellationToken = default)
    {
        await _resiliencePolicy.ExecuteAsync(async () =>
        {
            await _inner.RollbackToSavepointAsync(savepointName, cancellationToken);
        }, $"RollbackToSavepoint-{savepointName}", cancellationToken);
    }

    /// <summary>
    /// Disposes the unit of work asynchronously
    /// </summary>
    /// <returns>A task representing the asynchronous disposal operation</returns>
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
/// <remarks>
/// Implementations should provide retry logic and circuit breaker patterns to handle transient failures
/// </remarks>
public interface IConnectionResiliencePolicy
{
    /// <summary>
    /// Executes an asynchronous operation with resilience protection
    /// </summary>
    /// <param name="operation">The operation to execute</param>
    /// <param name="operationName">The name of the operation for logging and monitoring</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task ExecuteAsync(Func<Task> operation, string operationName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an asynchronous operation with resilience protection and returns a result
    /// </summary>
    /// <typeparam name="T">The type of the result</typeparam>
    /// <param name="operation">The operation to execute</param>
    /// <param name="operationName">The name of the operation for logging and monitoring</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation with the result</returns>
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
    private readonly ConnectionResilienceOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<DefaultConnectionResiliencePolicy> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ConnectionCircuitBreaker _circuitBreaker = new ConnectionCircuitBreaker(options.CircuitBreakerOptions, logger, timeProvider);
    private readonly ConnectionHealthMonitor? _healthMonitor = healthMonitor;

    /// <summary>
    /// Executes an asynchronous operation with resilience protection
    /// </summary>
    /// <param name="operation">The operation to execute</param>
    /// <param name="operationName">The name of the operation for logging and monitoring</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method applies exponential backoff retry logic with jitter and circuit breaker protection
    /// to handle transient failures. Health monitoring metrics are recorded when a health monitor is configured.
    /// </remarks>
    public async Task ExecuteAsync(Func<Task> operation, string operationName, CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation();
            return 0; // Dummy return value
        }, operationName, cancellationToken);
    }

    /// <summary>
    /// Executes an asynchronous operation with resilience protection and returns a result
    /// </summary>
    /// <typeparam name="T">The type of the result</typeparam>
    /// <param name="operation">The operation to execute</param>
    /// <param name="operationName">The name of the operation for logging and monitoring</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation</param>
    /// <returns>A task representing the asynchronous operation with the result</returns>
    /// <remarks>
    /// This method applies exponential backoff retry logic with jitter and circuit breaker protection
    /// to handle transient failures. Health monitoring metrics are recorded when a health monitor is configured.
    /// </remarks>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(operation, operationName, cancellationToken);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken)
    {
        var retryCount = 0;
        var maxRetries = _options.MaxRetries;
        var startTime = _timeProvider.GetUtcNow().DateTime;

        while (true)
        {
            // Check circuit breaker
            if (!await _circuitBreaker.CanExecuteAsync())
            {
                var duration = _timeProvider.GetUtcNow().DateTime - startTime;
                _healthMonitor?.RecordFailure(operationName,
                    new ConnectionResilienceException("Circuit breaker is open"), duration);
                throw new ConnectionResilienceException($"Circuit breaker is open for operation: {operationName}");
            }

            var operationStartTime = _timeProvider.GetUtcNow().DateTime;
            try
            {
                var result = await operation();
                var operationDuration = _timeProvider.GetUtcNow().DateTime - operationStartTime;

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
                var operationDuration = _timeProvider.GetUtcNow().DateTime - operationStartTime;
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
                var operationDuration = _timeProvider.GetUtcNow().DateTime - operationStartTime;

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
internal class ConnectionCircuitBreaker(CircuitBreakerOptions options, ILogger logger, TimeProvider timeProvider)
{
    private readonly CircuitBreakerOptions _options = options;
    private readonly ILogger _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider;
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
                ConnectionCircuitState.Open => _timeProvider.GetUtcNow().DateTime - _lastFailureTime >= _options.BreakDuration,
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
            _lastFailureTime = _timeProvider.GetUtcNow().DateTime;

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
    /// <summary>
    /// Gets or sets the maximum number of retry attempts for transient failures. Default is 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay between retry attempts. Default is 1 second.
    /// </summary>
    /// <remarks>
    /// Actual retry delays use exponential backoff with jitter based on this value
    /// </remarks>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum delay between retry attempts. Default is 30 seconds.
    /// </summary>
    /// <remarks>
    /// Caps the exponential backoff to prevent excessively long wait times
    /// </remarks>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the circuit breaker configuration options
    /// </summary>
    public CircuitBreakerOptions CircuitBreakerOptions { get; set; } = new();
}

/// <summary>
/// Circuit breaker specific options
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>
    /// Gets or sets the number of consecutive failures required to open the circuit breaker. Default is 5.
    /// </summary>
    /// <remarks>
    /// When this threshold is reached, the circuit breaker opens and subsequent operations are blocked
    /// until the break duration expires
    /// </remarks>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Gets or sets the duration to keep the circuit breaker open after it trips. Default is 30 seconds.
    /// </summary>
    /// <remarks>
    /// After this duration, the circuit breaker transitions to half-open state to test if the service has recovered
    /// </remarks>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Connection circuit breaker states
/// </summary>
public enum ConnectionCircuitState
{
    /// <summary>
    /// Circuit breaker is closed and operations are allowed to execute normally
    /// </summary>
    Closed,

    /// <summary>
    /// Circuit breaker is open and operations are blocked to prevent cascading failures
    /// </summary>
    Open,

    /// <summary>
    /// Circuit breaker is half-open and testing if the service has recovered
    /// </summary>
    HalfOpen
}

/// <summary>
/// Exception thrown when connection resilience fails
/// </summary>
public class ConnectionResilienceException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionResilienceException"/> class with a default error message
    /// </summary>
    public ConnectionResilienceException() : base("Connection resilience failed") { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionResilienceException"/> class with a specified error message
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    public ConnectionResilienceException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionResilienceException"/> class with a specified error message and inner exception
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public ConnectionResilienceException(string message, Exception innerException) : base(message, innerException) { }
}