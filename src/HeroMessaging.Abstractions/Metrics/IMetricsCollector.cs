namespace HeroMessaging.Abstractions.Metrics;

/// <summary>
/// Collects metrics about message processing
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Increments a counter metric
    /// </summary>
    /// <param name="name">Name of the counter</param>
    /// <param name="value">Value to increment by (default 1)</param>
    void IncrementCounter(string name, int value = 1);

    /// <summary>
    /// Records a duration metric
    /// </summary>
    /// <param name="name">Name of the duration metric</param>
    /// <param name="duration">Duration value</param>
    void RecordDuration(string name, TimeSpan duration);

    /// <summary>
    /// Records a numeric value metric
    /// </summary>
    /// <param name="name">Name of the value metric</param>
    /// <param name="value">Value to record</param>
    void RecordValue(string name, double value);
}