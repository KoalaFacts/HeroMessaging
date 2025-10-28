using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;

namespace HeroMessaging.Benchmarks;

/// <summary>
/// Custom configuration for HeroMessaging benchmarks
/// Includes memory diagnostics, multiple exporters, and custom thresholds
/// </summary>
public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        // Add memory diagnoser for allocation tracking
        AddDiagnoser(MemoryDiagnoser.Default);

        // Add various exporters for different formats
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(HtmlExporter.Default);
        AddExporter(CsvExporter.Default);
        AddExporter(RPlotExporter.Default);

        // Add console logger
        AddLogger(ConsoleLogger.Default);

        // Add columns for better result display
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.Error);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.P95);
        AddColumn(StatisticColumn.Min);
        AddColumn(StatisticColumn.Max);
        AddColumn(BaselineRatioColumn.RatioMean);

        // Order benchmarks by execution time
        WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));

        // Add summary style
        WithSummaryStyle(BenchmarkDotNet.Reports.SummaryStyle.Default);
    }
}

/// <summary>
/// Quick benchmark configuration for faster iteration during development
/// Reduces warmup and iteration counts
/// </summary>
public class QuickBenchmarkConfig : ManualConfig
{
    public QuickBenchmarkConfig()
    {
        AddJob(Job.ShortRun);
        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddLogger(ConsoleLogger.Default);
    }
}
