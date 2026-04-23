using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    public ConnectionHealthReport GetHealthReport()
    {
        return new ConnectionHealthReport
        {
            OverallStatus = GetOverallHealth(),
            Timestamp = _timeProvider.GetUtcNow(),
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
    /// Executes execute async.
    /// </summary>

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

                await Task.Delay(_options.HealthCheckInterval, _timeProvider, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during connection health monitoring");
                await Task.Delay(TimeSpan.FromMinutes(1), _timeProvider, stoppingToken);
            }
        }
    }

    private void CleanupOldMetrics()
    {
        var cutoff = _timeProvider.GetUtcNow() - _options.MetricsRetention;

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
    /// <summary>
    /// Represents successful requests.
    /// </summary>
    private long _successfulRequests;
    private long _failedRequests;
    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionHealthMetrics"/> class.
    /// </summary>

    public ConnectionHealthMetrics(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }
    /// <summary>
    /// Gets total requests.
    /// </summary>

    public long TotalRequests => _totalRequests;
    /// <summary>
    /// Gets successful requests.
    /// </summary>
    public long SuccessfulRequests => _successfulRequests;
    /// <summary>
    /// Gets failed requests.
    /// </summary>
    public long FailedRequests => _failedRequests;
    /// <summary>
    /// Gets failure rate.
    /// </summary>
    public double FailureRate => TotalRequests == 0 ? 0 : (double)FailedRequests / TotalRequests;
    /// <summary>
    /// Gets or sets last failure time.
    /// </summary>
    public DateTimeOffset LastFailureTime { get; private set; }
    /// <summary>
    /// Gets or sets last failure reason.
    /// </summary>
    public string LastFailureReason { get; private set; } = string.Empty;
    /// <summary>
    /// Gets or sets is circuit breaker open.
    /// </summary>
    public bool IsCircuitBreakerOpen { get; private set; }
    /// <summary>
    /// Gets average response time.
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
    /// Executes record success.
    /// </summary>

    public void RecordSuccess(TimeSpan duration)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _successfulRequests);

        _recentResults.Enqueue(new OperationResult
        {
            Success = true,
            Duration = duration,
            Timestamp = _timeProvider.GetUtcNow()
        });

        IsCircuitBreakerOpen = false;
    }
    /// <summary>
    /// Executes record failure.
    /// </summary>

    public void RecordFailure(Exception exception, TimeSpan duration)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _failedRequests);

        var now = _timeProvider.GetUtcNow();
        LastFailureTime = now;
        LastFailureReason = exception.Message;

        _recentResults.Enqueue(new OperationResult
        {
            Success = false,
            Duration = duration,
            Timestamp = now,
            Exception = exception
        });
    }
    /// <summary>
    /// Executes set circuit breaker state.
    /// </summary>

    public void SetCircuitBreakerState(bool isOpen)
    {
        IsCircuitBreakerOpen = isOpen;
    }
    /// <summary>
    /// Executes is unhealthy.
    /// </summary>

    public bool IsUnhealthy(double failureThreshold)
    {
        return FailureRate > failureThreshold || IsCircuitBreakerOpen;
    }
    /// <summary>
    /// Executes cleanup old data.
    /// </summary>

    public void CleanupOldData(DateTimeOffset cutoff)
    {
        while (_recentResults.TryPeek(out var result) && result.Timestamp < cutoff)
        {
            _recentResults.TryDequeue(out _);
        }
    }
    /// <summary>
    /// Represents the operation result record.
    /// </summary>

    private record OperationResult
    {
        /// <summary>
        /// Gets success.
        /// </summary>
        public bool Success { get; init; }
        /// <summary>
        /// Gets duration.
        /// </summary>
        public TimeSpan Duration { get; init; }
        public DateTimeOffset Timestamp { get; init; }
        /// <summary>
        /// Gets exception.
        /// </summary>
        public Exception? Exception { get; init; }
    }
}

/// <summary>
/// Configuration options for connection health monitoring
/// </summary>
public class ConnectionHealthOptions
{
    /// <summary>
    /// Gets or sets health check interval.
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>
    /// Gets or sets metrics retention.
    /// </summary>
    public TimeSpan MetricsRetention { get; set; } = TimeSpan.FromHours(1);
    /// <summary>
    /// Gets or sets unhealthy failure rate.
    /// </summary>
    public double UnhealthyFailureRate { get; set; } = 0.5;
}

/// <summary>
/// Health status enumeration
/// </summary>
public enum ConnectionHealthStatus
{
    /// <summary>
    /// Specifies unknown.
    /// </summary>
    Unknown,
    /// <summary>
    /// Specifies healthy.
    /// </summary>
    Healthy,
    /// <summary>
    /// Specifies degraded.
    /// </summary>
    Degraded,
    /// <summary>
    /// Specifies unhealthy.
    /// </summary>
    Unhealthy
}

/// <summary>
/// Comprehensive health report
/// </summary>
public class ConnectionHealthReport
{
    /// <summary>
    /// Gets or sets overall status.
    /// </summary>
    public ConnectionHealthStatus OverallStatus { get; set; }
    /// <summary>
    /// Gets or sets timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }
    /// <summary>
    /// Gets or sets operation metrics.
    /// </summary>
    public Dictionary<string, OperationHealthData> OperationMetrics { get; set; } = [];
}

/// <summary>
/// Health data for a specific operation
/// </summary>
public class OperationHealthData
{
    /// <summary>
    /// Gets or sets total requests.
    /// </summary>
    public long TotalRequests { get; set; }
    /// <summary>
    /// Gets or sets successful requests.
    /// </summary>
    public long SuccessfulRequests { get; set; }
    /// <summary>
    /// Gets or sets failed requests.
    /// </summary>
    public long FailedRequests { get; set; }
    /// <summary>
    /// Gets or sets failure rate.
    /// </summary>
    public double FailureRate { get; set; }
    /// <summary>
    /// Gets or sets average response time.
    /// </summary>
    public TimeSpan AverageResponseTime { get; set; }
    /// <summary>
    /// Gets or sets last failure time.
    /// </summary>
    public DateTimeOffset LastFailureTime { get; set; }
    /// <summary>
    /// Gets or sets last failure reason.
    /// </summary>
    public string LastFailureReason { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets circuit breaker state.
    /// </summary>
    public string CircuitBreakerState { get; set; } = "Closed";
}
