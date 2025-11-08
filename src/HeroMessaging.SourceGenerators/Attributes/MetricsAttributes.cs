// Copyright (c) HeroMessaging Contributors. All rights reserved.

using System;

namespace HeroMessaging.SourceGenerators;

/// <summary>
/// Generates metrics instrumentation for a method.
/// Creates counters, histograms, and gauges based on method execution.
/// </summary>
/// <example>
/// <code>
/// [InstrumentMethod(
///     InstrumentationType.Counter | InstrumentationType.Histogram,
///     MetricName = "orders.processed")]
/// public partial Task ProcessOrderAsync(string orderId, decimal amount);
///
/// // Generated implementation:
/// public partial async Task ProcessOrderAsync(string orderId, decimal amount)
/// {
///     var stopwatch = Stopwatch.StartNew();
///     var tags = new TagList
///     {
///         { "order_id", orderId },
///         { "amount_range", GetAmountRange(amount) }
///     };
///
///     try
///     {
///         var result = await ProcessOrderCoreAsync(orderId, amount);
///
///         _ordersProcessedCounter.Add(1, tags);
///         _orderProcessingDuration.Record(stopwatch.ElapsedMilliseconds, tags);
///
///         return result;
///     }
///     catch (Exception ex)
///     {
///         tags.Add("error", ex.GetType().Name);
///         _ordersProcessedCounter.Add(1, tags);
///         throw;
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class InstrumentMethodAttribute : Attribute
{
    /// <summary>
    /// The types of metrics to generate.
    /// </summary>
    public InstrumentationType Types { get; }

    /// <summary>
    /// The metric name (e.g., "orders.processed"). Defaults to class.method format.
    /// </summary>
    public string? MetricName { get; set; }

    /// <summary>
    /// Description of the metric for observability tools.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Unit for histogram measurements (e.g., "ms", "bytes", "items").
    /// </summary>
    public string Unit { get; set; } = "ms";

    /// <summary>
    /// Whether to include method parameters as metric tags (default: false for cardinality control).
    /// </summary>
    public bool TagParameters { get; set; } = false;

    /// <summary>
    /// Whether to tag errors with exception type (default: true).
    /// </summary>
    public bool TagErrors { get; set; } = true;

    public InstrumentMethodAttribute(InstrumentationType types)
    {
        Types = types;
    }
}

/// <summary>
/// Types of metrics to generate.
/// </summary>
[Flags]
public enum InstrumentationType
{
    /// <summary>
    /// Generate a Counter (monotonically increasing count).
    /// Use for: Total requests, total errors, total items processed.
    /// </summary>
    Counter = 1,

    /// <summary>
    /// Generate a Histogram (distribution of values).
    /// Use for: Request duration, payload size, batch size.
    /// </summary>
    Histogram = 2,

    /// <summary>
    /// Generate a Gauge (point-in-time value).
    /// Use for: Active connections, queue depth, cache size.
    /// </summary>
    Gauge = 4,

    /// <summary>
    /// Generate all metric types.
    /// </summary>
    All = Counter | Histogram | Gauge
}

/// <summary>
/// Marks a parameter to be used as a metric tag/label.
/// Helps control metric cardinality by explicitly choosing dimensions.
/// </summary>
/// <example>
/// <code>
/// [InstrumentMethod(InstrumentationType.Counter)]
/// public partial Task ProcessOrderAsync(
///     [MetricTag] string customerId,  // Included as tag
///     [MetricTag(Name = "order_type")] OrderType type,  // Custom tag name
///     string orderId);  // Not included (high cardinality)
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class MetricTagAttribute : Attribute
{
    /// <summary>
    /// Custom tag name (defaults to parameter name in snake_case).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Whether to bucket numeric values to reduce cardinality.
    /// Example: Amount 123.45 -> "100-200"
    /// </summary>
    public bool Bucket { get; set; } = false;
}

/// <summary>
/// Generates a metrics instrumentation class for a service.
/// Creates Meter, Counter, Histogram, and Gauge instances.
/// </summary>
/// <example>
/// <code>
/// [GenerateMetrics(MeterName = "HeroMessaging.Orders")]
/// public partial class OrderService
/// {
///     // Your service implementation
/// }
///
/// // Generated:
/// public partial class OrderService
/// {
///     private static readonly Meter _meter = new("HeroMessaging.Orders", "1.0.0");
///     private static readonly Counter&lt;long&gt; _ordersProcessed =
///         _meter.CreateCounter&lt;long&gt;("orders.processed", "count", "Total orders processed");
///     private static readonly Histogram&lt;double&gt; _orderDuration =
///         _meter.CreateHistogram&lt;double&gt;("orders.duration", "ms", "Order processing duration");
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class GenerateMetricsAttribute : Attribute
{
    /// <summary>
    /// The meter name for OpenTelemetry (e.g., "HeroMessaging.Orders").
    /// </summary>
    public string MeterName { get; set; } = string.Empty;

    /// <summary>
    /// The meter version (defaults to assembly version).
    /// </summary>
    public string? MeterVersion { get; set; }

    /// <summary>
    /// Whether to generate metrics for all public methods (default: false, opt-in only).
    /// </summary>
    public bool InstrumentAllMethods { get; set; } = false;

    public GenerateMetricsAttribute()
    {
    }

    public GenerateMetricsAttribute(string meterName)
    {
        MeterName = meterName;
    }
}

/// <summary>
/// Defines custom metric buckets for histogram distributions.
/// </summary>
/// <example>
/// <code>
/// [InstrumentMethod(InstrumentationType.Histogram)]
/// [MetricBuckets(10, 50, 100, 500, 1000, 5000)]
/// public partial Task ProcessBatchAsync(List&lt;Order&gt; orders);
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class MetricBucketsAttribute : Attribute
{
    public double[] Boundaries { get; }

    public MetricBucketsAttribute(params double[] boundaries)
    {
        Boundaries = boundaries;
    }
}
