using System.Diagnostics;
using System.Text;
using Xunit;

namespace HeroMessaging.Tests.TestUtilities;

// Placeholder for serialization test helpers
// TODO: Implement when serialization abstractions are available
public static class SerializationTestHelper
{
    public static void AssertObjectsEqual<T>(T expected, T actual) where T : class
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);

        if (expected is IEquatable<T> equatable)
        {
            Assert.True(equatable.Equals(actual),
                "Objects should be equal");
        }
        else
        {
            AssertPropertiesEqual(expected, actual);
        }
    }

    public static void AssertBytesNotEmpty(byte[] data)
    {
        Assert.NotNull(data);
        Assert.NotEmpty(data);
    }

    public static void ValidateJsonFormat(string json)
    {
        Assert.NotNull(json);
        Assert.NotEmpty(json);
        Assert.True(json.StartsWith("{") || json.StartsWith("["),
            "JSON should start with { or [");
        Assert.True(json.EndsWith("}") || json.EndsWith("]"),
            "JSON should end with } or ]");
    }

    public static PerformanceTestResult MeasurePerformance(string operationName, Action operation, int iterations = 1000)
    {
        var times = new List<double>();

        // Warm-up
        for (int i = 0; i < 10; i++)
        {
            operation();
        }

        // Measure
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            operation();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        return new PerformanceTestResult
        {
            OperationName = operationName,
            Iterations = iterations,
            AverageTime = times.Average(),
            MedianTime = GetMedian(times),
            P99Time = GetPercentile(times, 99),
            MinTime = times.Min(),
            MaxTime = times.Max()
        };
    }

    private static void AssertPropertiesEqual<T>(T expected, T actual)
    {
        var properties = typeof(T).GetProperties()
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

        foreach (var property in properties)
        {
            var expectedValue = property.GetValue(expected);
            var actualValue = property.GetValue(actual);

            if (expectedValue == null && actualValue == null)
                continue;

            Assert.Equal(expectedValue, actualValue);
        }
    }

    private static double GetMedian(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;

        if (sorted.Count % 2 == 0)
        {
            return (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        return sorted[mid];
    }

    private static double GetPercentile(List<double> values, int percentile)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
    }
}

public class PerformanceTestResult
{
    public string OperationName { get; set; } = string.Empty;
    public int Iterations { get; set; }
    public double AverageTime { get; set; }
    public double MedianTime { get; set; }
    public double P99Time { get; set; }
    public double MinTime { get; set; }
    public double MaxTime { get; set; }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Performance Test: {OperationName}");
        sb.AppendLine($"  Iterations: {Iterations}");
        sb.AppendLine($"  Average: {AverageTime:F3}ms");
        sb.AppendLine($"  Median: {MedianTime:F3}ms");
        sb.AppendLine($"  P99: {P99Time:F3}ms");
        sb.AppendLine($"  Range: {MinTime:F3}ms - {MaxTime:F3}ms");
        return sb.ToString();
    }
}
