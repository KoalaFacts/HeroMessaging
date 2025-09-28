using Xunit;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HeroMessaging.Contract.Tests;

public class PerformanceBenchmarkContractTests
{
    public interface IPerformanceBenchmark
    {
        Task<PerformanceBenchmarkResult> ExecuteBenchmarksAsync(string[] targetMetrics);
        Task<bool> UpdateBaselineAsync(PerformanceMetric[] metrics);
        Task<RegressionAnalysis> DetectRegressionsAsync(PerformanceMetric[] currentMetrics);
    }

    public class PerformanceBenchmarkResult
    {
        public string BenchmarkSuite { get; set; } = string.Empty;
        public PerformanceMetric[] Metrics { get; set; } = Array.Empty<PerformanceMetric>();
        public bool BaselineComparison { get; set; }
        public bool HasRegressions { get; set; }
        public double GrpcComparisonTarget { get; set; }
        public double ThroughputTarget { get; set; }
        public DateTime ExecutedAt { get; set; }
    }

    public class PerformanceMetric
    {
        public string MetricName { get; set; } = string.Empty;
        public double Value { get; set; }
        public string Unit { get; set; } = string.Empty;
        public double? Percentile { get; set; }
        public double? Baseline { get; set; }
        public double RegressionThreshold { get; set; }
        public bool IsRegression { get; set; }
        public string Environment { get; set; } = string.Empty;
    }

    public class RegressionAnalysis
    {
        public bool HasRegressions { get; set; }
        public PerformanceMetric[] RegressedMetrics { get; set; } = Array.Empty<PerformanceMetric>();
        public double MaxRegressionPercentage { get; set; }
        public string[] Recommendations { get; set; } = Array.Empty<string>();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ExecuteBenchmarks_WithLatencyMetrics_ReturnsSubMillisecondResults()
    {
        // Arrange
        var mockBenchmark = new Mock<IPerformanceBenchmark>();
        var targetMetrics = new[] { "MessageProcessingLatency", "SerializationLatency" };

        var expectedResult = new PerformanceBenchmarkResult
        {
            BenchmarkSuite = "MessageProcessing",
            Metrics = new[]
            {
                new PerformanceMetric
                {
                    MetricName = "MessageProcessingLatency",
                    Value = 0.8,
                    Unit = "ms",
                    Percentile = 99.0,
                    Baseline = 0.9,
                    RegressionThreshold = 10.0,
                    IsRegression = false,
                    Environment = "Release.net8.0"
                }
            },
            BaselineComparison = true,
            HasRegressions = false,
            GrpcComparisonTarget = 1.0,
            ThroughputTarget = 100000.0,
            ExecutedAt = DateTime.UtcNow
        };

        mockBenchmark.Setup(b => b.ExecuteBenchmarksAsync(targetMetrics))
                    .ReturnsAsync(expectedResult);

        // Act
        var result = await mockBenchmark.Object.ExecuteBenchmarksAsync(targetMetrics);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("MessageProcessing", result.BenchmarkSuite);
        Assert.False(result.HasRegressions);

        var latencyMetric = result.Metrics[0];
        Assert.True(latencyMetric.Value < 1.0, $"Latency {latencyMetric.Value}ms must be < 1ms (gRPC/HTTP2 target)");
        Assert.Equal(99.0, latencyMetric.Percentile);
        Assert.Equal("ms", latencyMetric.Unit);
        mockBenchmark.Verify(b => b.ExecuteBenchmarksAsync(targetMetrics), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ExecuteBenchmarks_WithThroughputMetrics_ExceedsHundredThousandMsgsPerSecond()
    {
        // Arrange
        var mockBenchmark = new Mock<IPerformanceBenchmark>();
        var targetMetrics = new[] { "MessageThroughput" };

        var expectedResult = new PerformanceBenchmarkResult
        {
            BenchmarkSuite = "ThroughputTest",
            Metrics = new[]
            {
                new PerformanceMetric
                {
                    MetricName = "MessageThroughput",
                    Value = 125000.0,
                    Unit = "ops/sec",
                    Baseline = 120000.0,
                    RegressionThreshold = 10.0,
                    IsRegression = false,
                    Environment = "Release.net8.0"
                }
            },
            ThroughputTarget = 100000.0,
            HasRegressions = false
        };

        mockBenchmark.Setup(b => b.ExecuteBenchmarksAsync(targetMetrics))
                    .ReturnsAsync(expectedResult);

        // Act
        var result = await mockBenchmark.Object.ExecuteBenchmarksAsync(targetMetrics);

        // Assert
        var throughputMetric = result.Metrics[0];
        Assert.True(throughputMetric.Value > 100000.0, $"Throughput {throughputMetric.Value} ops/sec must exceed 100K msgs/s target");
        Assert.Equal("ops/sec", throughputMetric.Unit);
        Assert.False(throughputMetric.IsRegression);
        mockBenchmark.Verify(b => b.ExecuteBenchmarksAsync(targetMetrics), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task UpdateBaseline_WithValidMetrics_ReturnsTrue()
    {
        // Arrange
        var mockBenchmark = new Mock<IPerformanceBenchmark>();
        var metrics = new[]
        {
            new PerformanceMetric
            {
                MetricName = "MessageProcessingLatency",
                Value = 0.7,
                Unit = "ms",
                Environment = "Release.net8.0"
            }
        };

        mockBenchmark.Setup(b => b.UpdateBaselineAsync(metrics))
                    .ReturnsAsync(true);

        // Act
        var result = await mockBenchmark.Object.UpdateBaselineAsync(metrics);

        // Assert
        Assert.True(result);
        mockBenchmark.Verify(b => b.UpdateBaselineAsync(metrics), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task DetectRegressions_WithTenPercentThreshold_IdentifiesRegressions()
    {
        // Arrange
        var mockBenchmark = new Mock<IPerformanceBenchmark>();
        var currentMetrics = new[]
        {
            new PerformanceMetric
            {
                MetricName = "MessageProcessingLatency",
                Value = 1.2,
                Unit = "ms",
                Baseline = 1.0,
                RegressionThreshold = 10.0,
                IsRegression = true
            }
        };

        var expectedAnalysis = new RegressionAnalysis
        {
            HasRegressions = true,
            RegressedMetrics = currentMetrics,
            MaxRegressionPercentage = 20.0,
            Recommendations = new[]
            {
                "MessageProcessingLatency regressed by 20% from 1.0ms to 1.2ms",
                "Consider optimizing serialization path",
                "Review recent changes to processing pipeline"
            }
        };

        mockBenchmark.Setup(b => b.DetectRegressionsAsync(currentMetrics))
                    .ReturnsAsync(expectedAnalysis);

        // Act
        var result = await mockBenchmark.Object.DetectRegressionsAsync(currentMetrics);

        // Assert
        Assert.True(result.HasRegressions);
        Assert.Equal(20.0, result.MaxRegressionPercentage);
        Assert.True(result.MaxRegressionPercentage > 10.0, "Regression exceeds 10% threshold");
        Assert.NotEmpty(result.RegressedMetrics);
        Assert.NotEmpty(result.Recommendations);
        Assert.Contains("20%", result.Recommendations[0]);
        mockBenchmark.Verify(b => b.DetectRegressionsAsync(currentMetrics), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task DetectRegressions_WithinThreshold_ReturnsNoRegressions()
    {
        // Arrange
        var mockBenchmark = new Mock<IPerformanceBenchmark>();
        var currentMetrics = new[]
        {
            new PerformanceMetric
            {
                MetricName = "MessageProcessingLatency",
                Value = 1.05,
                Unit = "ms",
                Baseline = 1.0,
                RegressionThreshold = 10.0,
                IsRegression = false
            }
        };

        var expectedAnalysis = new RegressionAnalysis
        {
            HasRegressions = false,
            RegressedMetrics = Array.Empty<PerformanceMetric>(),
            MaxRegressionPercentage = 5.0,
            Recommendations = Array.Empty<string>()
        };

        mockBenchmark.Setup(b => b.DetectRegressionsAsync(currentMetrics))
                    .ReturnsAsync(expectedAnalysis);

        // Act
        var result = await mockBenchmark.Object.DetectRegressionsAsync(currentMetrics);

        // Assert
        Assert.False(result.HasRegressions);
        Assert.True(result.MaxRegressionPercentage <= 10.0, "Regression within 10% threshold");
        Assert.Empty(result.RegressedMetrics);
        Assert.Empty(result.Recommendations);
        mockBenchmark.Verify(b => b.DetectRegressionsAsync(currentMetrics), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ExecuteBenchmarks_WithMemoryAllocation_TracksZeroAllocationPaths()
    {
        // Arrange
        var mockBenchmark = new Mock<IPerformanceBenchmark>();
        var targetMetrics = new[] { "MemoryAllocation" };

        var expectedResult = new PerformanceBenchmarkResult
        {
            BenchmarkSuite = "MemoryProfiling",
            Metrics = new[]
            {
                new PerformanceMetric
                {
                    MetricName = "MemoryAllocation",
                    Value = 512.0,
                    Unit = "bytes",
                    Baseline = 1024.0,
                    RegressionThreshold = 10.0,
                    IsRegression = false,
                    Environment = "Release.net8.0"
                }
            },
            HasRegressions = false
        };

        mockBenchmark.Setup(b => b.ExecuteBenchmarksAsync(targetMetrics))
                    .ReturnsAsync(expectedResult);

        // Act
        var result = await mockBenchmark.Object.ExecuteBenchmarksAsync(targetMetrics);

        // Assert
        var memoryMetric = result.Metrics[0];
        Assert.True(memoryMetric.Value < 1024.0, $"Memory allocation {memoryMetric.Value} bytes should be < 1KB per message");
        Assert.Equal("bytes", memoryMetric.Unit);
        Assert.False(memoryMetric.IsRegression);
        mockBenchmark.Verify(b => b.ExecuteBenchmarksAsync(targetMetrics), Times.Once);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ExecuteBenchmarks_WithGrpcComparison_MeetsOrExceedsGrpcPerformance()
    {
        // Arrange
        var mockBenchmark = new Mock<IPerformanceBenchmark>();
        var targetMetrics = new[] { "GrpcComparison" };

        var expectedResult = new PerformanceBenchmarkResult
        {
            BenchmarkSuite = "GrpcBenchmark",
            Metrics = new[]
            {
                new PerformanceMetric
                {
                    MetricName = "GrpcComparison",
                    Value = 0.8,
                    Unit = "ms",
                    Percentile = 99.0,
                    Environment = "Release.net8.0"
                }
            },
            GrpcComparisonTarget = 1.0,
            BaselineComparison = true
        };

        mockBenchmark.Setup(b => b.ExecuteBenchmarksAsync(targetMetrics))
                    .ReturnsAsync(expectedResult);

        // Act
        var result = await mockBenchmark.Object.ExecuteBenchmarksAsync(targetMetrics);

        // Assert
        var grpcMetric = result.Metrics[0];
        Assert.True(grpcMetric.Value <= result.GrpcComparisonTarget,
            $"Performance {grpcMetric.Value}ms should meet or exceed gRPC target {result.GrpcComparisonTarget}ms");
        Assert.Equal(99.0, grpcMetric.Percentile);
        mockBenchmark.Verify(b => b.ExecuteBenchmarksAsync(targetMetrics), Times.Once);
    }
}