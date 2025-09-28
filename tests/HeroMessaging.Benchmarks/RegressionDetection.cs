using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace HeroMessaging.Benchmarks;

/// <summary>
/// Performance regression detection system with 10% threshold enforcement
/// Compares current performance against established baselines and reports regressions
/// </summary>
public class RegressionDetection
{
    private const double RegressionThresholdPercent = 10.0;
    private const string BaselineFileName = "performance-baseline.json";
    private const string ResultsDirectory = "benchmark-results";

    /// <summary>
    /// Detects performance regressions by comparing current results against baseline
    /// </summary>
    /// <param name="currentResults">Current benchmark summary</param>
    /// <param name="baselinePath">Path to baseline performance data</param>
    /// <returns>Regression analysis results</returns>
    public async Task<RegressionAnalysis> DetectRegressionsAsync(Summary currentResults, string? baselinePath = null)
    {
        baselinePath ??= Path.Combine(ResultsDirectory, BaselineFileName);

        var baseline = await LoadBaselineAsync(baselinePath);
        var analysis = new RegressionAnalysis();

        foreach (var benchmark in currentResults.BenchmarksCases)
        {
            var currentMetrics = ExtractMetrics(currentResults, benchmark);

            if (baseline.TryGetValue(benchmark.Descriptor.WorkloadMethodDisplayInfo, out var baselineMetrics))
            {
                var regression = AnalyzeRegression(baselineMetrics, currentMetrics);

                if (regression.HasRegression)
                {
                    analysis.Regressions.Add(regression);
                }

                analysis.Comparisons.Add(regression);
            }
            else
            {
                analysis.NewBenchmarks.Add(benchmark.Descriptor.WorkloadMethodDisplayInfo);
            }
        }

        analysis.OverallRegressionDetected = analysis.Regressions.Any();
        return analysis;
    }

    /// <summary>
    /// Updates the performance baseline with current results
    /// </summary>
    /// <param name="results">Current benchmark results to use as new baseline</param>
    /// <param name="baselinePath">Path to save baseline data</param>
    public async Task UpdateBaselineAsync(Summary results, string? baselinePath = null)
    {
        baselinePath ??= Path.Combine(ResultsDirectory, BaselineFileName);

        var baseline = new Dictionary<string, PerformanceMetrics>();

        foreach (var benchmark in results.BenchmarksCases)
        {
            var metrics = ExtractMetrics(results, benchmark);
            baseline[benchmark.Descriptor.WorkloadMethodDisplayInfo] = metrics;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);

        var json = JsonSerializer.Serialize(baseline, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(baselinePath, json);
    }

    /// <summary>
    /// Generates a comprehensive performance trend analysis
    /// </summary>
    /// <param name="historicalResults">List of historical benchmark summaries</param>
    /// <returns>Trend analysis results</returns>
    public PerformanceTrendAnalysis AnalyzeTrends(IEnumerable<Summary> historicalResults)
    {
        var trends = new PerformanceTrendAnalysis();
        var resultsList = historicalResults.ToList();

        if (resultsList.Count < 2)
        {
            trends.Message = "Insufficient data for trend analysis (minimum 2 data points required)";
            return trends;
        }

        var benchmarkGroups = resultsList
            .SelectMany(summary => summary.BenchmarksCases.Select(bc => new { Summary = summary, BenchmarkCase = bc }))
            .GroupBy(x => x.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo);

        foreach (var group in benchmarkGroups)
        {
            var dataPoints = group
                .Select(x => new TrendDataPoint
                {
                    Timestamp = DateTime.UtcNow, // In real implementation, extract from summary
                    Metrics = ExtractMetrics(x.Summary, x.BenchmarkCase)
                })
                .OrderBy(x => x.Timestamp)
                .ToList();

            if (dataPoints.Count >= 2)
            {
                var trend = CalculateTrend(dataPoints);
                trends.BenchmarkTrends[group.Key] = trend;
            }
        }

        return trends;
    }

    /// <summary>
    /// Generates a performance regression report
    /// </summary>
    /// <param name="analysis">Regression analysis results</param>
    /// <returns>Formatted regression report</returns>
    public string GenerateRegressionReport(RegressionAnalysis analysis)
    {
        var report = new List<string>
        {
            "=== Performance Regression Report ===",
            $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            $"Regression Threshold: {RegressionThresholdPercent}%",
            ""
        };

        if (analysis.OverallRegressionDetected)
        {
            report.Add($"⚠️  REGRESSIONS DETECTED: {analysis.Regressions.Count} benchmark(s) show performance degradation");
            report.Add("");

            foreach (var regression in analysis.Regressions)
            {
                report.Add($"❌ {regression.BenchmarkName}");
                report.Add($"   Mean: {regression.BaselineMetrics.MeanNanoseconds:N0}ns → {regression.CurrentMetrics.MeanNanoseconds:N0}ns ({regression.MeanRegressionPercent:F1}% slower)");

                if (regression.P99RegressionPercent > RegressionThresholdPercent)
                {
                    report.Add($"   P99:  {regression.BaselineMetrics.P99Nanoseconds:N0}ns → {regression.CurrentMetrics.P99Nanoseconds:N0}ns ({regression.P99RegressionPercent:F1}% slower)");
                }

                if (regression.ThroughputRegressionPercent > RegressionThresholdPercent)
                {
                    report.Add($"   Throughput: {regression.BaselineMetrics.ThroughputOpsPerSecond:N0} → {regression.CurrentMetrics.ThroughputOpsPerSecond:N0} ops/s ({regression.ThroughputRegressionPercent:F1}% decrease)");
                }

                report.Add("");
            }
        }
        else
        {
            report.Add("✅ No performance regressions detected");
            report.Add("");
        }

        if (analysis.NewBenchmarks.Any())
        {
            report.Add($"🆕 New benchmarks added: {string.Join(", ", analysis.NewBenchmarks)}");
            report.Add("");
        }

        report.Add($"Total comparisons: {analysis.Comparisons.Count}");
        report.Add($"Regressions: {analysis.Regressions.Count}");
        report.Add($"Improvements: {analysis.Comparisons.Count(c => !c.HasRegression && c.MeanRegressionPercent < 0)}");

        return string.Join(Environment.NewLine, report);
    }

    /// <summary>
    /// Configures BenchmarkDotNet for regression detection
    /// </summary>
    /// <returns>Configuration for regression detection benchmarks</returns>
    public IConfig CreateRegressionDetectionConfig()
    {
        return ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(Job.Default
                .WithId("RegressionDetection")
                .WithIterationCount(15)
                .WithWarmupCount(5)
                .WithInvocationCount(1))
            .AddExporter(JsonExporter.Full)
            .AddLogger(ConsoleLogger.Default)
            .WithOption(ConfigOptions.DisableOptimizationsValidator, true);
    }

    private async Task<Dictionary<string, PerformanceMetrics>> LoadBaselineAsync(string baselinePath)
    {
        if (!File.Exists(baselinePath))
        {
            return new Dictionary<string, PerformanceMetrics>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(baselinePath);
            return JsonSerializer.Deserialize<Dictionary<string, PerformanceMetrics>>(json)
                   ?? new Dictionary<string, PerformanceMetrics>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load baseline from {baselinePath}: {ex.Message}", ex);
        }
    }

    private PerformanceMetrics ExtractMetrics(Summary summary, BenchmarkDotNet.Running.BenchmarkCase benchmarkCase)
    {
        var report = summary.Reports.FirstOrDefault(r => r.BenchmarkCase == benchmarkCase);

        if (report?.ResultStatistics == null)
        {
            return new PerformanceMetrics();
        }

        var stats = report.ResultStatistics;

        return new PerformanceMetrics
        {
            MeanNanoseconds = stats.Mean,
            P99Nanoseconds = stats.Percentiles.P95, // Using P95 as closest to P99
            StandardDeviationNanoseconds = stats.StandardDeviation,
            ThroughputOpsPerSecond = CalculateThroughput(stats.Mean),
            AllocatedBytesPerOperation = report.GcStats.GetBytesAllocatedPerOperation(benchmarkCase) ?? 0
        };
    }

    private double CalculateThroughput(double meanNanoseconds)
    {
        if (meanNanoseconds <= 0) return 0;
        return 1_000_000_000.0 / meanNanoseconds; // ops per second
    }

    private PerformanceRegression AnalyzeRegression(PerformanceMetrics baseline, PerformanceMetrics current)
    {
        var regression = new PerformanceRegression
        {
            BenchmarkName = "", // Will be set by caller
            BaselineMetrics = baseline,
            CurrentMetrics = current
        };

        if (baseline.MeanNanoseconds > 0)
        {
            regression.MeanRegressionPercent = ((current.MeanNanoseconds - baseline.MeanNanoseconds) / baseline.MeanNanoseconds) * 100;
        }

        if (baseline.P99Nanoseconds > 0)
        {
            regression.P99RegressionPercent = ((current.P99Nanoseconds - baseline.P99Nanoseconds) / baseline.P99Nanoseconds) * 100;
        }

        if (baseline.ThroughputOpsPerSecond > 0)
        {
            regression.ThroughputRegressionPercent = ((baseline.ThroughputOpsPerSecond - current.ThroughputOpsPerSecond) / baseline.ThroughputOpsPerSecond) * 100;
        }

        regression.HasRegression = regression.MeanRegressionPercent > RegressionThresholdPercent
                                 || regression.P99RegressionPercent > RegressionThresholdPercent
                                 || regression.ThroughputRegressionPercent > RegressionThresholdPercent;

        return regression;
    }

    private BenchmarkTrend CalculateTrend(List<TrendDataPoint> dataPoints)
    {
        var trend = new BenchmarkTrend();

        if (dataPoints.Count < 2) return trend;

        var latest = dataPoints.Last();
        var previous = dataPoints[dataPoints.Count - 2];

        trend.LatestMeanNanoseconds = latest.Metrics.MeanNanoseconds;
        trend.PreviousMeanNanoseconds = previous.Metrics.MeanNanoseconds;

        if (previous.Metrics.MeanNanoseconds > 0)
        {
            trend.ChangePercent = ((latest.Metrics.MeanNanoseconds - previous.Metrics.MeanNanoseconds) / previous.Metrics.MeanNanoseconds) * 100;
        }

        // Simple linear trend calculation
        if (dataPoints.Count >= 3)
        {
            var meanChanges = new List<double>();
            for (int i = 1; i < dataPoints.Count; i++)
            {
                var prev = dataPoints[i - 1].Metrics.MeanNanoseconds;
                var curr = dataPoints[i].Metrics.MeanNanoseconds;
                if (prev > 0)
                {
                    meanChanges.Add(((curr - prev) / prev) * 100);
                }
            }

            if (meanChanges.Any())
            {
                trend.TrendDirection = meanChanges.Average() switch
                {
                    > 5 => TrendDirection.Degrading,
                    < -5 => TrendDirection.Improving,
                    _ => TrendDirection.Stable
                };
            }
        }

        return trend;
    }
}

/// <summary>
/// Results of performance regression analysis
/// </summary>
public class RegressionAnalysis
{
    public bool OverallRegressionDetected { get; set; }
    public List<PerformanceRegression> Regressions { get; set; } = new();
    public List<PerformanceRegression> Comparisons { get; set; } = new();
    public List<string> NewBenchmarks { get; set; } = new();
}

/// <summary>
/// Performance regression details for a specific benchmark
/// </summary>
public class PerformanceRegression
{
    public string BenchmarkName { get; set; } = string.Empty;
    public PerformanceMetrics BaselineMetrics { get; set; } = new();
    public PerformanceMetrics CurrentMetrics { get; set; } = new();
    public double MeanRegressionPercent { get; set; }
    public double P99RegressionPercent { get; set; }
    public double ThroughputRegressionPercent { get; set; }
    public bool HasRegression { get; set; }
}

/// <summary>
/// Performance metrics for a benchmark
/// </summary>
public class PerformanceMetrics
{
    public double MeanNanoseconds { get; set; }
    public double P99Nanoseconds { get; set; }
    public double StandardDeviationNanoseconds { get; set; }
    public double ThroughputOpsPerSecond { get; set; }
    public long AllocatedBytesPerOperation { get; set; }
}

/// <summary>
/// Performance trend analysis results
/// </summary>
public class PerformanceTrendAnalysis
{
    public Dictionary<string, BenchmarkTrend> BenchmarkTrends { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Trend data for a single benchmark over time
/// </summary>
public class BenchmarkTrend
{
    public double LatestMeanNanoseconds { get; set; }
    public double PreviousMeanNanoseconds { get; set; }
    public double ChangePercent { get; set; }
    public TrendDirection TrendDirection { get; set; } = TrendDirection.Unknown;
}

/// <summary>
/// Data point for trend analysis
/// </summary>
public class TrendDataPoint
{
    public DateTime Timestamp { get; set; }
    public PerformanceMetrics Metrics { get; set; } = new();
}

/// <summary>
/// Performance trend direction
/// </summary>
public enum TrendDirection
{
    Unknown,
    Improving,
    Stable,
    Degrading
}