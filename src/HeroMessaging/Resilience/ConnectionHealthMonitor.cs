using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace HeroMessaging.Resilience;

/// <summary>
/// Monitors connection health and provides metrics for resilience policies
/// </summary>
public class ConnectionHealthMonitor(
    ILogger<ConnectionHealthMonitor> logger,
    ConnectionHealthOptions? options = null) : BackgroundService
{
    private readonly ILogger<ConnectionHealthMonitor> _logger = logger;
    private readonly ConnectionHealthOptions _options = options ?? new ConnectionHealthOptions();
    private readonly ConcurrentDictionary<string, ConnectionHealthMetrics> _metrics = new();


    /// <summary>
    /// Gets health metrics for a specific operation
    /// </summary>
    public ConnectionHealthMetrics GetMetrics(string operationName)
    {
        return _metrics.GetOrAdd(operationName, _ => new ConnectionHealthMetrics());
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
            Timestamp = DateTime.UtcNow,
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
        var cutoff = DateTime.UtcNow - _options.MetricsRetention;

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
    private long _totalRequests;
    private long _successfulRequests;
    private long _failedRequests;
    private DateTime _lastFailureTime;
    private string _lastFailureReason = string.Empty;
    private bool _circuitBreakerOpen;

    public long TotalRequests => _totalRequests;
    public long SuccessfulRequests => _successfulRequests;
    public long FailedRequests => _failedRequests;
    public double FailureRate => TotalRequests == 0 ? 0 : (double)FailedRequests / TotalRequests;
    public DateTime LastFailureTime => _lastFailureTime;
    public string LastFailureReason => _lastFailureReason;
    public bool IsCircuitBreakerOpen => _circuitBreakerOpen;

    public TimeSpan AverageResponseTime
    {
        get
        {
            var results = _recentResults.ToArray();
            if (!results.Any()) return TimeSpan.Zero;

            return TimeSpan.FromMilliseconds(results.Average(r => r.Duration.TotalMilliseconds));
        }
    }

    public void RecordSuccess(TimeSpan duration)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _successfulRequests);

        _recentResults.Enqueue(new OperationResult
        {
            Success = true,
            Duration = duration,
            Timestamp = DateTime.UtcNow
        });

        _circuitBreakerOpen = false;
    }

    public void RecordFailure(Exception exception, TimeSpan duration)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _failedRequests);

        _lastFailureTime = DateTime.UtcNow;
        _lastFailureReason = exception.Message;

        _recentResults.Enqueue(new OperationResult
        {
            Success = false,
            Duration = duration,
            Timestamp = DateTime.UtcNow,
            Exception = exception
        });
    }

    public void SetCircuitBreakerState(bool isOpen)
    {
        _circuitBreakerOpen = isOpen;
    }

    public bool IsUnhealthy(double failureThreshold)
    {
        return FailureRate > failureThreshold || IsCircuitBreakerOpen;
    }

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
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan MetricsRetention { get; set; } = TimeSpan.FromHours(1);
    public double UnhealthyFailureRate { get; set; } = 0.5;
}

/// <summary>
/// Health status enumeration
/// </summary>
public enum ConnectionHealthStatus
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy
}

/// <summary>
/// Comprehensive health report
/// </summary>
public class ConnectionHealthReport
{
    public ConnectionHealthStatus OverallStatus { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, OperationHealthData> OperationMetrics { get; set; } = new();
}

/// <summary>
/// Health data for a specific operation
/// </summary>
public class OperationHealthData
{
    public long TotalRequests { get; set; }
    public long SuccessfulRequests { get; set; }
    public long FailedRequests { get; set; }
    public double FailureRate { get; set; }
    public TimeSpan AverageResponseTime { get; set; }
    public DateTime LastFailureTime { get; set; }
    public string LastFailureReason { get; set; } = string.Empty;
    public string CircuitBreakerState { get; set; } = "Closed";
}