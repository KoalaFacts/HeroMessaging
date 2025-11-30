using HeroMessaging.Abstractions.Processing;

namespace HeroMessaging.Processing;

/// <summary>
/// Thread-safe metrics collector for message processors.
/// Centralizes duration tracking, success/failure counting, and metrics calculation.
/// </summary>
internal sealed class ProcessorMetricsCollector
{
    private readonly TimeProvider _timeProvider;
    private readonly int _metricsHistorySize;
    private readonly List<long> _durations;
    private long _processedCount;
    private long _failedCount;

#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessorMetricsCollector"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider for timing measurements.</param>
    /// <param name="metricsHistorySize">Maximum number of duration samples to keep. Defaults to 100.</param>
    public ProcessorMetricsCollector(TimeProvider timeProvider, int metricsHistorySize = ProcessingConstants.DefaultMetricsHistorySize)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _metricsHistorySize = metricsHistorySize;
        _durations = new List<long>(metricsHistorySize);
    }

    /// <summary>
    /// Records a successful operation with its duration.
    /// </summary>
    /// <param name="elapsedMilliseconds">The duration of the operation in milliseconds.</param>
    public void RecordSuccess(long elapsedMilliseconds)
    {
        lock (_lock)
        {
            _processedCount++;
            _durations.Add(elapsedMilliseconds);
            if (_durations.Count > _metricsHistorySize)
            {
                _durations.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Records a failed operation.
    /// </summary>
    public void RecordFailure()
    {
        lock (_lock)
        {
            _failedCount++;
        }
    }

    /// <summary>
    /// Executes an action and records its metrics.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ExecuteWithMetricsAsync(Func<Task> action)
    {
        var startTime = _timeProvider.GetTimestamp();
        try
        {
            await action().ConfigureAwait(false);
            var elapsedMs = _timeProvider.GetElapsedTime(startTime).TotalMilliseconds;
            RecordSuccess((long)elapsedMs);
        }
        catch
        {
            RecordFailure();
            throw;
        }
    }

    /// <summary>
    /// Executes a function and records its metrics.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="func">The function to execute.</param>
    /// <returns>The result of the function.</returns>
    public async Task<T> ExecuteWithMetricsAsync<T>(Func<Task<T>> func)
    {
        var startTime = _timeProvider.GetTimestamp();
        try
        {
            var result = await func().ConfigureAwait(false);
            var elapsedMs = _timeProvider.GetElapsedTime(startTime).TotalMilliseconds;
            RecordSuccess((long)elapsedMs);
            return result;
        }
        catch
        {
            RecordFailure();
            throw;
        }
    }

    /// <summary>
    /// Gets the current metrics snapshot.
    /// </summary>
    /// <returns>A snapshot of the current metrics.</returns>
    public IProcessorMetrics GetMetrics()
    {
        lock (_lock)
        {
            return new ProcessorMetrics
            {
                ProcessedCount = _processedCount,
                FailedCount = _failedCount,
                AverageDuration = _durations.Count > 0
                    ? TimeSpan.FromMilliseconds(_durations.Average())
                    : TimeSpan.Zero
            };
        }
    }

    /// <summary>
    /// Gets query processor specific metrics.
    /// </summary>
    /// <returns>A snapshot of the current metrics for query processors.</returns>
    public IQueryProcessorMetrics GetQueryMetrics()
    {
        lock (_lock)
        {
            return new QueryProcessorMetrics
            {
                ProcessedCount = _processedCount,
                FailedCount = _failedCount,
                AverageDuration = _durations.Count > 0
                    ? TimeSpan.FromMilliseconds(_durations.Average())
                    : TimeSpan.Zero,
                CacheHitRate = 0 // TODO: Add cache tracking if needed
            };
        }
    }
}

/// <summary>
/// Standard processor metrics implementation.
/// </summary>
public class ProcessorMetrics : IProcessorMetrics
{
    public long ProcessedCount { get; init; }
    public long FailedCount { get; init; }
    public TimeSpan AverageDuration { get; init; }
}

/// <summary>
/// Query processor metrics implementation with cache hit rate tracking.
/// </summary>
public class QueryProcessorMetrics : IQueryProcessorMetrics
{
    public long ProcessedCount { get; init; }
    public long FailedCount { get; init; }
    public TimeSpan AverageDuration { get; init; }
    public double CacheHitRate { get; init; }
}
