using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Metrics;
using HeroMessaging.Abstractions.Processing;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that collects metrics about message processing
/// </summary>
public class MetricsDecorator(IMessageProcessor inner, IMetricsCollector metricsCollector) : MessageProcessorDecorator(inner)
{
    private readonly IMetricsCollector _metricsCollector = metricsCollector;

    public override async ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default)
    {
        var messageType = message.GetType().Name;
        var stopwatch = Stopwatch.StartNew();

        _metricsCollector.IncrementCounter($"messages.{messageType}.started");

        try
        {
            var result = await _inner.ProcessAsync(message, context, cancellationToken);
            stopwatch.Stop();

            if (result.Success)
            {
                _metricsCollector.IncrementCounter($"messages.{messageType}.succeeded");
                _metricsCollector.RecordDuration($"messages.{messageType}.duration", stopwatch.Elapsed);
            }
            else
            {
                _metricsCollector.IncrementCounter($"messages.{messageType}.failed");
                if (context.RetryCount > 0)
                {
                    _metricsCollector.IncrementCounter($"messages.{messageType}.retried", context.RetryCount);
                }
            }

            return result;
        }
        catch (Exception)
        {
            stopwatch.Stop();
            _metricsCollector.IncrementCounter($"messages.{messageType}.exceptions");
            _metricsCollector.RecordDuration($"messages.{messageType}.duration", stopwatch.Elapsed);
            throw;
        }
    }
}

/// <summary>
/// In-memory implementation of <see cref="IMetricsCollector"/> for collecting application metrics
/// </summary>
/// <remarks>
/// This implementation stores metrics in memory using concurrent collections for thread-safe access.
/// Suitable for development, testing, and single-instance deployments. For production distributed
/// systems, consider using a dedicated metrics provider like Prometheus, Application Insights, or DataDog.
///
/// Collected metrics include:
/// - Counters: Incrementing values (e.g., messages processed, errors occurred)
/// - Durations: Time measurements (e.g., processing time, latency)
/// - Values: Arbitrary numeric measurements (e.g., queue depth, payload size)
///
/// Performance characteristics:
/// - Lock-free counter increments using ConcurrentDictionary
/// - O(1) metric recording operations
/// - Memory grows with unique metric names and recorded values
/// - No automatic metric expiration or aggregation
///
/// Usage recommendations:
/// - Development: Useful for debugging and local testing
/// - Production: Use for low-traffic scenarios or as a fallback
/// - Distributed systems: Replace with distributed metrics provider
/// </remarks>
public class InMemoryMetricsCollector : IMetricsCollector
{
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<TimeSpan>> _durations = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<double>> _values = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncrementCounter(string name, int value = 1)
    {
        _counters.AddOrUpdate(name, value, (_, current) => current + value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordDuration(string name, TimeSpan duration)
    {
        var bag = _durations.GetOrAdd(name, _ => new ConcurrentBag<TimeSpan>());
        bag.Add(duration);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordValue(string name, double value)
    {
        var bag = _values.GetOrAdd(name, _ => new ConcurrentBag<double>());
        bag.Add(value);
    }

    public Dictionary<string, object> GetSnapshot()
    {
        var snapshot = new Dictionary<string, object>();

        foreach (var counter in _counters)
            snapshot[counter.Key] = counter.Value;

        foreach (var duration in _durations)
        {
            var values = duration.Value.ToArray();
            if (values.Length > 0)
            {
                snapshot[$"{duration.Key}.count"] = values.Length;
                snapshot[$"{duration.Key}.avg_ms"] = values.Average(d => d.TotalMilliseconds);
                snapshot[$"{duration.Key}.max_ms"] = values.Max(d => d.TotalMilliseconds);
                snapshot[$"{duration.Key}.min_ms"] = values.Min(d => d.TotalMilliseconds);
            }
        }

        return snapshot;
    }
}