namespace HeroMessaging.Abstractions.Metrics;

/// <summary>
/// Interface for collecting metrics about message processing operations.
/// Enables observability, monitoring, and performance analysis of the messaging system.
/// </summary>
/// <remarks>
/// Implement this interface to integrate HeroMessaging with your metrics platform:
/// - Prometheus (counters, histograms, gauges)
/// - Application Insights (custom metrics, telemetry)
/// - CloudWatch (metrics, dashboards)
/// - StatsD / Graphite (time series data)
/// - Custom metrics systems
///
/// The metrics collector is designed to be:
/// - High-performance: Minimal overhead (&lt;1μs per metric call)
/// - Non-blocking: Metrics collection must not slow down message processing
/// - Thread-safe: Safe to call from multiple concurrent threads
/// - Fire-and-forget: Failed metrics collection should not fail message processing
///
/// HeroMessaging automatically collects metrics for:
/// - Message processing counts and rates (commands, queries, events)
/// - Processing duration (p50, p95, p99 latencies)
/// - Error rates and failure counts
/// - Queue depths and throughput
/// - Cache hit rates (for query processors)
/// - Outbox/inbox processing metrics
///
/// <code>
/// // Implement for your metrics platform
/// public class PrometheusMetricsCollector : IMetricsCollector
/// {
///     private readonly Counter _messageCounter;
///     private readonly Histogram _durationHistogram;
///     private readonly Gauge _queueDepthGauge;
///
///     public PrometheusMetricsCollector()
///     {
///         _messageCounter = Metrics.CreateCounter(
///             "messaging_messages_total",
///             "Total messages processed",
///             new CounterConfiguration { LabelNames = new[] { "type", "status" } }
///         );
///
///         _durationHistogram = Metrics.CreateHistogram(
///             "messaging_duration_seconds",
///             "Message processing duration",
///             new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.001, 2, 10) }
///         );
///
///         _queueDepthGauge = Metrics.CreateGauge(
///             "messaging_queue_depth",
///             "Current queue depth",
///             new GaugeConfiguration { LabelNames = new[] { "queue" } }
///         );
///     }
///
///     public void IncrementCounter(string name, int value = 1)
///     {
///         _messageCounter.WithLabels(name, "success").Inc(value);
///     }
///
///     public void RecordDuration(string name, TimeSpan duration)
///     {
///         _durationHistogram.Observe(duration.TotalSeconds);
///     }
///
///     public void RecordValue(string name, double value)
///     {
///         _queueDepthGauge.WithLabels(name).Set(value);
///     }
/// }
///
/// // Register in DI
/// services.AddSingleton&lt;IMetricsCollector, PrometheusMetricsCollector&gt;();
/// </code>
///
/// Metric naming conventions:
/// - Use lowercase with underscores: "messages_processed_total"
/// - Include unit in name: "duration_seconds", "size_bytes"
/// - Use consistent prefixes: "messaging_*", "queue_*", "outbox_*"
/// - Follow platform conventions (Prometheus, CloudWatch, etc.)
/// </remarks>
public interface IMetricsCollector
{
    /// <summary>
    /// Increments a counter metric by the specified value.
    /// Counters are monotonically increasing values used to track totals (messages processed, errors, etc.).
    /// </summary>
    /// <param name="name">
    /// The name of the counter metric.
    /// Should be descriptive and follow your metrics platform's naming conventions.
    /// Examples: "messages_processed", "validation_errors", "cache_hits"
    /// </param>
    /// <param name="value">
    /// The value to increment the counter by.
    /// Default is 1 (increment by one).
    /// Use values &gt; 1 for batch operations or aggregated counts.
    /// </param>
    /// <remarks>
    /// Counters are used to track cumulative totals that only increase:
    /// - Total messages processed
    /// - Total errors encountered
    /// - Total cache hits/misses
    /// - Total bytes sent/received
    ///
    /// Common counter patterns:
    /// <code>
    /// // Increment by 1 (default)
    /// metricsCollector.IncrementCounter("messages_processed");
    ///
    /// // Increment by custom value (batch processing)
    /// metricsCollector.IncrementCounter("messages_processed", batchSize);
    ///
    /// // Track success/failure
    /// if (result.Success)
    ///     metricsCollector.IncrementCounter("messages_succeeded");
    /// else
    ///     metricsCollector.IncrementCounter("messages_failed");
    ///
    /// // Track by message type
    /// metricsCollector.IncrementCounter($"messages_{message.GetType().Name}");
    /// </code>
    ///
    /// Performance considerations:
    /// - Must complete in &lt;1μs to avoid impacting message throughput
    /// - Should not allocate memory in hot paths
    /// - Must be thread-safe (called from concurrent message processors)
    /// - Failures in metrics collection should not throw exceptions
    ///
    /// Best practices:
    /// - Use consistent naming: messages_processed (not msgs_proc)
    /// - Include metric type in name: messages_total, errors_total
    /// - Add labels/tags for dimensions (message type, queue name, etc.)
    /// - Counter should always increase (use gauges for values that decrease)
    /// </remarks>
    void IncrementCounter(string name, int value = 1);

    /// <summary>
    /// Records a duration measurement for tracking latencies and performance.
    /// Durations are typically aggregated into histograms, percentiles (p50, p95, p99), or averages.
    /// </summary>
    /// <param name="name">
    /// The name of the duration metric.
    /// Should indicate what operation's duration is being measured.
    /// Examples: "message_processing_duration", "database_query_duration", "serialization_duration"
    /// </param>
    /// <param name="duration">
    /// The duration to record.
    /// Typically measured using Stopwatch or DateTime differences.
    /// Should be the actual elapsed time, not a scaled value.
    /// </param>
    /// <remarks>
    /// Duration metrics track how long operations take:
    /// - Message processing time (end-to-end)
    /// - Component-specific timing (validation, serialization, handler execution)
    /// - Database query time
    /// - External API call time
    /// - Queue wait time
    ///
    /// Common duration patterns:
    /// <code>
    /// // Measure operation duration
    /// var stopwatch = Stopwatch.StartNew();
    /// try
    /// {
    ///     await ProcessMessageAsync(message);
    /// }
    /// finally
    /// {
    ///     stopwatch.Stop();
    ///     metricsCollector.RecordDuration("message_processing", stopwatch.Elapsed);
    /// }
    ///
    /// // Measure component timing
    /// var start = DateTime.UtcNow;
    /// var result = await serializer.SerializeAsync(message);
    /// metricsCollector.RecordDuration("serialization", DateTime.UtcNow - start);
    ///
    /// // Track database operations
    /// using (metricsCollector.MeasureDuration("database_query"))
    /// {
    ///     await repository.SaveAsync(entity);
    /// }
    /// </code>
    ///
    /// Metrics platforms typically aggregate durations into:
    /// - Percentiles: p50 (median), p95, p99 (tail latencies)
    /// - Averages: mean, moving average
    /// - Histograms: distribution of durations across buckets
    /// - Min/Max: fastest and slowest operations
    ///
    /// Performance targets for HeroMessaging:
    /// - Framework overhead: &lt;1ms (p99)
    /// - Message processing: &lt;10ms (p95) for typical handlers
    /// - Serialization: &lt;100μs (p99) for messages &lt;10KB
    /// - Validation: &lt;1ms (p95) without database calls
    ///
    /// Best practices:
    /// - Use consistent units (seconds recommended, not milliseconds)
    /// - Include operation name in metric name
    /// - Configure appropriate histogram buckets for your latency ranges
    /// - Track both successful and failed operation durations
    /// - Use labels/tags for operation type, message type, etc.
    /// </remarks>
    void RecordDuration(string name, TimeSpan duration);

    /// <summary>
    /// Records a numeric value for tracking gauges, measurements, and instantaneous readings.
    /// Values can increase or decrease and represent point-in-time measurements.
    /// </summary>
    /// <param name="name">
    /// The name of the value metric.
    /// Should describe what is being measured.
    /// Examples: "queue_depth", "memory_usage_bytes", "active_connections", "cache_size"
    /// </param>
    /// <param name="value">
    /// The numeric value to record.
    /// Can be any double value (positive, negative, zero).
    /// Units should be consistent for each metric name.
    /// </param>
    /// <remarks>
    /// Value metrics (gauges) track measurements that can go up or down:
    /// - Queue depth (number of pending messages)
    /// - Memory usage (bytes allocated)
    /// - Active connections (concurrent processors)
    /// - Cache size (number of cached items)
    /// - CPU usage (percentage)
    /// - Temperature, latency, throughput rates
    ///
    /// Common value metric patterns:
    /// <code>
    /// // Record queue depth
    /// var queueDepth = await queueRepository.GetDepthAsync("orders");
    /// metricsCollector.RecordValue("queue_depth_orders", queueDepth);
    ///
    /// // Record memory usage
    /// var memoryUsed = GC.GetTotalMemory(forceFullCollection: false);
    /// metricsCollector.RecordValue("memory_usage_bytes", memoryUsed);
    ///
    /// // Record throughput rate
    /// var messagesPerSecond = processedCount / elapsedSeconds;
    /// metricsCollector.RecordValue("throughput_messages_per_second", messagesPerSecond);
    ///
    /// // Record percentage
    /// var cacheHitRate = (double)cacheHits / (cacheHits + cacheMisses);
    /// metricsCollector.RecordValue("cache_hit_rate", cacheHitRate);
    ///
    /// // Record resource utilization
    /// var cpuUsage = Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
    /// metricsCollector.RecordValue("cpu_usage_milliseconds", cpuUsage);
    /// </code>
    ///
    /// Differences from counters:
    /// - Counters: Always increase (total messages processed)
    /// - Values/Gauges: Can increase or decrease (current queue depth)
    /// - Counters: Cumulative totals
    /// - Values/Gauges: Point-in-time measurements
    ///
    /// Common use cases in HeroMessaging:
    /// - Queue depths (pending, processing, completed)
    /// - Outbox pending message count
    /// - Active handler count
    /// - Cache size and hit rates
    /// - Connection pool size
    /// - Thread pool utilization
    ///
    /// Best practices:
    /// - Include units in metric name: bytes, seconds, count, percent
    /// - Use consistent units (don't mix KB and MB)
    /// - Record regularly for trending (not just on changes)
    /// - Consider min/max/avg aggregations
    /// - Use labels/tags for dimensions (queue name, processor type)
    /// - For rates, calculate consistently (per second, per minute)
    /// </remarks>
    void RecordValue(string name, double value);
}