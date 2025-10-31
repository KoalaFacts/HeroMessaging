using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace HeroMessaging.Resilience;

/// <summary>
/// Monitors connection health and provides metrics for resilience policies
/// </summary>
public class ConnectionHealthMonitor(
    ILogger<ConnectionHealthMonitor> logger,
    TimeProvider timeProvider,
    ConnectionHealthOptions? options = null) : BackgroundService
{
    private readonly ILogger<ConnectionHealthMonitor> _logger = logger;
    private readonly ConnectionHealthOptions _options = options ?? new ConnectionHealthOptions();
    private readonly ConcurrentDictionary<string, ConnectionHealthMetrics> _metrics = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));


    /// <summary>
    /// Gets health metrics for a specific operation
    /// </summary>
    public ConnectionHealthMetrics GetMetrics(string operationName)
    {
        return _metrics.GetOrAdd(operationName, _ => new ConnectionHealthMetrics(_timeProvider));
    }

    /// <summary>
    /// Records a successful operation
    /// </summary>
    public void RecordSuccess(string operationName, TimeSpan duration)
    {
        var metrics = GetMetrics(operationName);
        metrics.RecordSuccess(duration);
    }

    /// <summary>
    /// Records a failed operation
    /// </summary>
    public void RecordFailure(string operationName, Exception exception, TimeSpan duration)
    {
        var metrics = GetMetrics(operationName);
        metrics.RecordFailure(exception, duration);

        _logger.LogWarning(exception,
            "Connection failure for operation {OperationName} after {Duration}ms. Current failure rate: {FailureRate:P}",
            operationName, duration.TotalMilliseconds, metrics.FailureRate);
    }

    /// <summary>
    /// Gets overall health status
    /// </summary>
    public ConnectionHealthStatus GetOverallHealth()
    {
        if (!_metrics.Any())
            return ConnectionHealthStatus.Unknown;

        var unhealthyOperations = _metrics
            .Where(kvp => kvp.Value.IsUnhealthy(_options.UnhealthyFailureRate))
            .ToList();

        if (unhealthyOperations.Count == 0)
            return ConnectionHealthStatus.Healthy;

        var totalOperations = _metrics.Count;
        var unhealthyRatio = (double)unhealthyOperations.Count / totalOperations;

        return unhealthyRatio > 0.5
            ? ConnectionHealthStatus.Unhealthy
            : ConnectionHealthStatus.Degraded;
    }

    /// <summary>
    /// Gets health report with detailed metrics
    /// </summary>
    /// <returns>A comprehensive health report containing overall status and per-operation metrics</returns>
    public ConnectionHealthReport GetHealthReport()
    {
        return new ConnectionHealthReport
        {
            OverallStatus = GetOverallHealth(),
            Timestamp = _timeProvider.GetUtcNow().DateTime,
            OperationMetrics = _metrics.ToDictionary(
                kvp => kvp.Key,
                kvp => new OperationHealthData
                {
                    TotalRequests = kvp.Value.TotalRequests,
                    SuccessfulRequests = kvp.Value.SuccessfulRequests,
                    FailedRequests = kvp.Value.FailedRequests,
                    FailureRate = kvp.Value.FailureRate,
                    AverageResponseTime = kvp.Value.AverageResponseTime,
                    LastFailureTime = kvp.Value.LastFailureTime,
                    LastFailureReason = kvp.Value.LastFailureReason,
                    CircuitBreakerState = kvp.Value.IsCircuitBreakerOpen ? "Open" : "Closed"
                })
        };
    }

    /// <summary>
    /// Background service execution loop that periodically checks connection health and cleans up old metrics
    /// </summary>
    /// <param name="stoppingToken">Cancellation token to stop the background service</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <remarks>
    /// This method runs continuously and performs health checks at the interval specified in the health options.
    /// It logs warnings for degraded health and errors for unhealthy status.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Periodic health check logging
                var report = GetHealthReport();

                if (report.OverallStatus == ConnectionHealthStatus.Unhealthy)
                {
                    _logger.LogError("Connection health is UNHEALTHY. {UnhealthyOperations} operations are failing.",
                        report.OperationMetrics.Count(o => o.Value.FailureRate > _options.UnhealthyFailureRate));
                }
                else if (report.OverallStatus == ConnectionHealthStatus.Degraded)
                {
                    _logger.LogWarning("Connection health is DEGRADED. Some operations experiencing failures.");
                }

                // Cleanup old metrics
                CleanupOldMetrics();

                await Task.Delay(_options.HealthCheckInterval, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during connection health monitoring");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private void CleanupOldMetrics()
    {
        var cutoff = _timeProvider.GetUtcNow().DateTime - _options.MetricsRetention;

        foreach (var metrics in _metrics.Values)
        {
            metrics.CleanupOldData(cutoff);
        }
    }
}

/// <summary>
/// Tracks connection health metrics for a specific operation
/// </summary>
public class ConnectionHealthMetrics
{
    private readonly ConcurrentQueue<OperationResult> _recentResults = new();
    private readonly TimeProvider _timeProvider;
    private long _totalRequests;
    private long _successfulRequests;
    private long _failedRequests;
    private DateTime _lastFailureTime;
    private string _lastFailureReason = string.Empty;
    private bool _circuitBreakerOpen;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionHealthMetrics"/> class
    /// </summary>
    /// <param name="timeProvider">The time provider for timestamp management</param>
    public ConnectionHealthMetrics(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Gets the total number of operation requests
    /// </summary>
    public long TotalRequests => _totalRequests;

    /// <summary>
    /// Gets the number of successful operation requests
    /// </summary>
    public long SuccessfulRequests => _successfulRequests;

    /// <summary>
    /// Gets the number of failed operation requests
    /// </summary>
    public long FailedRequests => _failedRequests;

    /// <summary>
    /// Gets the failure rate as a value between 0 and 1
    /// </summary>
    public double FailureRate => TotalRequests == 0 ? 0 : (double)FailedRequests / TotalRequests;

    /// <summary>
    /// Gets the timestamp of the last operation failure
    /// </summary>
    public DateTime LastFailureTime => _lastFailureTime;

    /// <summary>
    /// Gets the error message from the last operation failure
    /// </summary>
    public string LastFailureReason => _lastFailureReason;

    /// <summary>
    /// Gets a value indicating whether the circuit breaker is currently open for this operation
    /// </summary>
    public bool IsCircuitBreakerOpen => _circuitBreakerOpen;

    /// <summary>
    /// Gets the average response time for recent operations
    /// </summary>
    public TimeSpan AverageResponseTime
    {
        get
        {
            var results = _recentResults.ToArray();
            if (!results.Any()) return TimeSpan.Zero;

            return TimeSpan.FromMilliseconds(results.Average(r => r.Duration.TotalMilliseconds));
        }
    }

    /// <summary>
    /// Records a successful operation execution
    /// </summary>
    /// <param name="duration">The duration of the operation</param>
    public void RecordSuccess(TimeSpan duration)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _successfulRequests);

        _recentResults.Enqueue(new OperationResult
        {
            Success = true,
            Duration = duration,
            Timestamp = _timeProvider.GetUtcNow().DateTime
        });

        _circuitBreakerOpen = false;
    }

    /// <summary>
    /// Records a failed operation execution
    /// </summary>
    /// <param name="exception">The exception that caused the failure</param>
    /// <param name="duration">The duration of the operation before failure</param>
    public void RecordFailure(Exception exception, TimeSpan duration)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _failedRequests);

        var now = _timeProvider.GetUtcNow().DateTime;
        _lastFailureTime = now;
        _lastFailureReason = exception.Message;

        _recentResults.Enqueue(new OperationResult
        {
            Success = false,
            Duration = duration,
            Timestamp = now,
            Exception = exception
        });
    }

    /// <summary>
    /// Sets the circuit breaker state for this operation
    /// </summary>
    /// <param name="isOpen">True if the circuit breaker should be open; otherwise, false</param>
    public void SetCircuitBreakerState(bool isOpen)
    {
        _circuitBreakerOpen = isOpen;
    }

    /// <summary>
    /// Determines if the operation is unhealthy based on the failure threshold
    /// </summary>
    /// <param name="failureThreshold">The failure rate threshold above which the operation is considered unhealthy</param>
    /// <returns>True if the operation is unhealthy; otherwise, false</returns>
    public bool IsUnhealthy(double failureThreshold)
    {
        return FailureRate > failureThreshold || IsCircuitBreakerOpen;
    }

    /// <summary>
    /// Removes operation results older than the specified cutoff time
    /// </summary>
    /// <param name="cutoff">The cutoff time; results older than this will be removed</param>
    public void CleanupOldData(DateTime cutoff)
    {
        while (_recentResults.TryPeek(out var result) && result.Timestamp < cutoff)
        {
            _recentResults.TryDequeue(out _);
        }
    }

    private record OperationResult
    {
        public bool Success { get; init; }
        public TimeSpan Duration { get; init; }
        public DateTime Timestamp { get; init; }
        public Exception? Exception { get; init; }
    }
}

/// <summary>
/// Configuration options for connection health monitoring
/// </summary>
public class ConnectionHealthOptions
{
    /// <summary>
    /// Gets or sets the interval between health checks. Default is 1 minute.
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the duration to retain metrics data. Default is 1 hour.
    /// </summary>
    /// <remarks>
    /// Metrics older than this duration are automatically cleaned up to prevent unbounded memory growth
    /// </remarks>
    public TimeSpan MetricsRetention { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the failure rate threshold for considering an operation unhealthy. Default is 0.5 (50%).
    /// </summary>
    /// <remarks>
    /// Operations with a failure rate above this threshold are marked as unhealthy
    /// </remarks>
    public double UnhealthyFailureRate { get; set; } = 0.5;
}

/// <summary>
/// Health status enumeration
/// </summary>
public enum ConnectionHealthStatus
{
    /// <summary>
    /// Health status is unknown, typically when no operations have been performed yet
    /// </summary>
    Unknown,

    /// <summary>
    /// All operations are performing normally with low failure rates
    /// </summary>
    Healthy,

    /// <summary>
    /// Some operations are experiencing elevated failure rates but system is still operational
    /// </summary>
    Degraded,

    /// <summary>
    /// Multiple operations are failing at high rates indicating serious connectivity issues
    /// </summary>
    Unhealthy
}

/// <summary>
/// Comprehensive health report
/// </summary>
public class ConnectionHealthReport
{
    /// <summary>
    /// Gets or sets the overall health status of all monitored operations
    /// </summary>
    public ConnectionHealthStatus OverallStatus { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the health report was generated
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the health metrics for each monitored operation, keyed by operation name
    /// </summary>
    public Dictionary<string, OperationHealthData> OperationMetrics { get; set; } = new();
}

/// <summary>
/// Health data for a specific operation
/// </summary>
public class OperationHealthData
{
    /// <summary>
    /// Gets or sets the total number of requests for this operation
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// Gets or sets the number of successful requests for this operation
    /// </summary>
    public long SuccessfulRequests { get; set; }

    /// <summary>
    /// Gets or sets the number of failed requests for this operation
    /// </summary>
    public long FailedRequests { get; set; }

    /// <summary>
    /// Gets or sets the failure rate for this operation as a value between 0 and 1
    /// </summary>
    public double FailureRate { get; set; }

    /// <summary>
    /// Gets or sets the average response time for this operation
    /// </summary>
    public TimeSpan AverageResponseTime { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the last failure for this operation
    /// </summary>
    public DateTime LastFailureTime { get; set; }

    /// <summary>
    /// Gets or sets the error message from the last failure for this operation
    /// </summary>
    public string LastFailureReason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the circuit breaker state for this operation (Open or Closed)
    /// </summary>
    public string CircuitBreakerState { get; set; } = "Closed";
}