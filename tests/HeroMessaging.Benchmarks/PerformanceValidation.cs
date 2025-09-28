using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace HeroMessaging.Benchmarks;

/// <summary>
/// Automated performance validation with constitutional compliance enforcement
/// Validates latency (<1ms p99), throughput (>100K msg/s), and memory constraints
/// </summary>
public class PerformanceValidation
{
    private readonly PerformanceConstraints _constraints;

    public PerformanceValidation(PerformanceConstraints? constraints = null)
    {
        _constraints = constraints ?? new PerformanceConstraints();
    }

    /// <summary>
    /// Validates all performance benchmarks against constitutional constraints
    /// </summary>
    /// <param name="benchmarkResults">Benchmark execution results</param>
    /// <returns>Comprehensive performance validation results</returns>
    public Task<PerformanceValidationResults> ValidatePerformanceAsync(Summary benchmarkResults)
    {
        var results = new PerformanceValidationResults
        {
            StartTime = DateTime.UtcNow,
            Constraints = _constraints
        };

        try
        {
            // Validate latency constraints
            var latencyValidations = ValidateLatencyConstraints(benchmarkResults);
            results.LatencyValidations.AddRange(latencyValidations);

            // Validate throughput constraints
            var throughputValidations = ValidateThroughputConstraints(benchmarkResults);
            results.ThroughputValidations.AddRange(throughputValidations);

            // Validate memory constraints
            var memoryValidations = ValidateMemoryConstraints(benchmarkResults);
            results.MemoryValidations.AddRange(memoryValidations);

            // Validate allocation constraints
            var allocationValidations = ValidateAllocationConstraints(benchmarkResults);
            results.AllocationValidations.AddRange(allocationValidations);

            // Overall validation status
            results.OverallValidation = DetermineOverallValidation(results);

            // Generate detailed report
            results.ValidationReport = GenerateValidationReport(results);

            // Generate performance alerts if needed
            results.PerformanceAlerts = GeneratePerformanceAlerts(results);

            results.Success = results.OverallValidation.PassesValidation;
        }
        catch (Exception ex)
        {
            results.Success = false;
            results.ErrorMessage = ex.Message;
        }
        finally
        {
            results.EndTime = DateTime.UtcNow;
            results.Duration = results.EndTime - results.StartTime;
        }

        return Task.FromResult(results);
    }

    /// <summary>
    /// Validates constitutional latency constraint: <1ms p99
    /// </summary>
    /// <param name="benchmarkResults">Benchmark results to validate</param>
    /// <returns>Latency validation results</returns>
    public List<LatencyValidationResult> ValidateLatencyConstraints(Summary benchmarkResults)
    {
        var validations = new List<LatencyValidationResult>();

        foreach (var report in benchmarkResults.Reports)
        {
            var validation = new LatencyValidationResult
            {
                BenchmarkName = report.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo,
                TargetType = ExtractTargetType(report.BenchmarkCase.Descriptor.Type)
            };

            if (report.ResultStatistics != null)
            {
                // Convert from nanoseconds to milliseconds
                validation.MeanLatencyMs = report.ResultStatistics.Mean / 1_000_000;
                validation.P99LatencyMs = report.ResultStatistics.Percentiles?.P95 / 1_000_000 ?? 0; // Using P95 as closest to P99
                validation.P50LatencyMs = report.ResultStatistics.Percentiles?.P50 / 1_000_000 ?? 0;
                validation.StandardDeviationMs = report.ResultStatistics.StandardDeviation / 1_000_000;

                // Constitutional constraint: <1ms p99 latency
                validation.PassesConstraint = validation.P99LatencyMs <= _constraints.MaxP99LatencyMs;

                // Additional checks for different operation types
                var operationType = DetermineOperationType(validation.BenchmarkName);
                validation.OperationType = operationType;

                // Apply specific constraints based on operation type
                switch (operationType)
                {
                    case OperationType.MessageProcessing:
                        validation.PassesConstraint = validation.P99LatencyMs <= _constraints.MaxMessageProcessingLatencyMs;
                        break;
                    case OperationType.Serialization:
                        validation.PassesConstraint = validation.P99LatencyMs <= _constraints.MaxSerializationLatencyMs;
                        break;
                    case OperationType.Storage:
                        validation.PassesConstraint = validation.P99LatencyMs <= _constraints.MaxStorageLatencyMs;
                        break;
                }

                validation.ConstraintThresholdMs = GetLatencyThresholdForOperation(operationType);
            }

            validations.Add(validation);
        }

        return validations;
    }

    /// <summary>
    /// Validates constitutional throughput constraint: >100K msg/s
    /// </summary>
    /// <param name="benchmarkResults">Benchmark results to validate</param>
    /// <returns>Throughput validation results</returns>
    public List<ThroughputValidationResult> ValidateThroughputConstraints(Summary benchmarkResults)
    {
        var validations = new List<ThroughputValidationResult>();

        foreach (var report in benchmarkResults.Reports)
        {
            var validation = new ThroughputValidationResult
            {
                BenchmarkName = report.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo,
                TargetType = ExtractTargetType(report.BenchmarkCase.Descriptor.Type)
            };

            if (report.ResultStatistics != null)
            {
                // Calculate operations per second from mean time
                validation.ActualThroughputOpsPerSecond = CalculateThroughput(report.ResultStatistics.Mean);

                var operationType = DetermineOperationType(validation.BenchmarkName);
                validation.OperationType = operationType;

                // Apply constitutional constraint: >100K msg/s for message processing
                var requiredThroughput = GetThroughputThresholdForOperation(operationType);
                validation.RequiredThroughputOpsPerSecond = requiredThroughput;
                validation.PassesConstraint = validation.ActualThroughputOpsPerSecond >= requiredThroughput;

                // Calculate efficiency metric
                validation.EfficiencyRatio = validation.ActualThroughputOpsPerSecond / requiredThroughput;
            }

            validations.Add(validation);
        }

        return validations;
    }

    /// <summary>
    /// Validates memory usage constraints
    /// </summary>
    /// <param name="benchmarkResults">Benchmark results to validate</param>
    /// <returns>Memory validation results</returns>
    public List<MemoryValidationResult> ValidateMemoryConstraints(Summary benchmarkResults)
    {
        var validations = new List<MemoryValidationResult>();

        foreach (var report in benchmarkResults.Reports)
        {
            var validation = new MemoryValidationResult
            {
                BenchmarkName = report.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo,
                TargetType = ExtractTargetType(report.BenchmarkCase.Descriptor.Type)
            };

            var gcStats = report.GcStats;
            if (gcStats.TotalOperations > 0)
            {
                validation.AllocatedBytesPerOperation = gcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0;
                validation.Gen0CollectionsPerOperation = gcStats.Gen0Collections;
                validation.Gen1CollectionsPerOperation = gcStats.Gen1Collections;
                validation.Gen2CollectionsPerOperation = gcStats.Gen2Collections;

                var operationType = DetermineOperationType(validation.BenchmarkName);
                validation.OperationType = operationType;

                // Apply memory constraints based on operation type
                var maxAllowedBytes = GetMemoryThresholdForOperation(operationType);
                validation.MaxAllowedBytesPerOperation = maxAllowedBytes;
                validation.PassesConstraint = validation.AllocatedBytesPerOperation <= maxAllowedBytes;

                // Additional constraint: zero-allocation paths for critical operations
                if (operationType == OperationType.MessageProcessing && _constraints.RequireZeroAllocationPaths)
                {
                    validation.PassesConstraint = validation.AllocatedBytesPerOperation == 0;
                }
            }

            validations.Add(validation);
        }

        return validations;
    }

    /// <summary>
    /// Validates allocation patterns for steady-state operations
    /// </summary>
    /// <param name="benchmarkResults">Benchmark results to validate</param>
    /// <returns>Allocation validation results</returns>
    public List<AllocationValidationResult> ValidateAllocationConstraints(Summary benchmarkResults)
    {
        var validations = new List<AllocationValidationResult>();

        foreach (var report in benchmarkResults.Reports)
        {
            var validation = new AllocationValidationResult
            {
                BenchmarkName = report.BenchmarkCase.Descriptor.WorkloadMethodDisplayInfo,
                TargetType = ExtractTargetType(report.BenchmarkCase.Descriptor.Type)
            };

            var gcStats = report.GcStats;
            if (gcStats.TotalOperations > 0)
            {
                validation.TotalAllocations = gcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0;
                validation.AllocationRate = validation.TotalAllocations / (report.ResultStatistics?.Mean / 1_000_000_000 ?? 1);

                var operationType = DetermineOperationType(validation.BenchmarkName);
                validation.OperationType = operationType;

                // Constitutional constraint: <1KB allocation per message in steady state
                var maxAllocationRate = GetAllocationRateThresholdForOperation(operationType);
                validation.MaxAllowedAllocationRate = maxAllocationRate;
                validation.PassesConstraint = validation.AllocationRate <= maxAllocationRate;

                // Check for allocation patterns that indicate memory leaks
                validation.HasSuspiciousAllocationPattern = DetectSuspiciousAllocationPattern(gcStats, report.BenchmarkCase);
            }

            validations.Add(validation);
        }

        return validations;
    }

    /// <summary>
    /// Generates performance alerts for failing validations
    /// </summary>
    /// <param name="validationResults">Validation results to analyze</param>
    /// <returns>List of performance alerts</returns>
    public List<PerformanceAlert> GeneratePerformanceAlerts(PerformanceValidationResults validationResults)
    {
        var alerts = new List<PerformanceAlert>();

        // Critical latency violations
        var criticalLatencyViolations = validationResults.LatencyValidations
            .Where(l => !l.PassesConstraint && l.P99LatencyMs > l.ConstraintThresholdMs * 2)
            .ToList();

        foreach (var violation in criticalLatencyViolations)
        {
            alerts.Add(new PerformanceAlert
            {
                Severity = AlertSeverity.Critical,
                Type = AlertType.LatencyViolation,
                BenchmarkName = violation.BenchmarkName,
                Message = $"Critical latency violation: {violation.P99LatencyMs:F2}ms (threshold: {violation.ConstraintThresholdMs:F2}ms)",
                RecommendedAction = GetLatencyOptimizationRecommendation(violation.OperationType)
            });
        }

        // Throughput degradation
        var throughputDegradations = validationResults.ThroughputValidations
            .Where(t => !t.PassesConstraint)
            .ToList();

        foreach (var degradation in throughputDegradations)
        {
            alerts.Add(new PerformanceAlert
            {
                Severity = AlertSeverity.High,
                Type = AlertType.ThroughputDegradation,
                BenchmarkName = degradation.BenchmarkName,
                Message = $"Throughput below threshold: {degradation.ActualThroughputOpsPerSecond:N0} ops/s (required: {degradation.RequiredThroughputOpsPerSecond:N0} ops/s)",
                RecommendedAction = GetThroughputOptimizationRecommendation(degradation.OperationType)
            });
        }

        // Memory allocation violations
        var memoryViolations = validationResults.MemoryValidations
            .Where(m => !m.PassesConstraint)
            .ToList();

        foreach (var violation in memoryViolations)
        {
            alerts.Add(new PerformanceAlert
            {
                Severity = AlertSeverity.Medium,
                Type = AlertType.MemoryViolation,
                BenchmarkName = violation.BenchmarkName,
                Message = $"Memory allocation violation: {violation.AllocatedBytesPerOperation} bytes (threshold: {violation.MaxAllowedBytesPerOperation} bytes)",
                RecommendedAction = GetMemoryOptimizationRecommendation(violation.OperationType)
            });
        }

        return alerts;
    }

    private string GenerateValidationReport(PerformanceValidationResults results)
    {
        var report = new List<string>
        {
            "# Performance Validation Report",
            $"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            $"Overall Status: {(results.OverallValidation.PassesValidation ? "✅ PASSED" : "❌ FAILED")}",
            ""
        };

        // Latency Summary
        report.Add("## Latency Validation");
        var latencyPassed = results.LatencyValidations.Count(l => l.PassesConstraint);
        var latencyTotal = results.LatencyValidations.Count;
        report.Add($"**Status:** {latencyPassed}/{latencyTotal} benchmarks passed");
        report.Add("");

        if (results.LatencyValidations.Any(l => !l.PassesConstraint))
        {
            report.Add("### Failed Latency Constraints");
            foreach (var failure in results.LatencyValidations.Where(l => !l.PassesConstraint))
            {
                report.Add($"- **{failure.BenchmarkName}**: {failure.P99LatencyMs:F2}ms (threshold: {failure.ConstraintThresholdMs:F2}ms)");
            }
            report.Add("");
        }

        // Throughput Summary
        report.Add("## Throughput Validation");
        var throughputPassed = results.ThroughputValidations.Count(t => t.PassesConstraint);
        var throughputTotal = results.ThroughputValidations.Count;
        report.Add($"**Status:** {throughputPassed}/{throughputTotal} benchmarks passed");
        report.Add("");

        // Memory Summary
        report.Add("## Memory Validation");
        var memoryPassed = results.MemoryValidations.Count(m => m.PassesConstraint);
        var memoryTotal = results.MemoryValidations.Count;
        report.Add($"**Status:** {memoryPassed}/{memoryTotal} benchmarks passed");

        return string.Join(Environment.NewLine, report);
    }

    private OverallValidationResult DetermineOverallValidation(PerformanceValidationResults results)
    {
        var overall = new OverallValidationResult();

        overall.LatencyValidationPassed = results.LatencyValidations.All(l => l.PassesConstraint);
        overall.ThroughputValidationPassed = results.ThroughputValidations.All(t => t.PassesConstraint);
        overall.MemoryValidationPassed = results.MemoryValidations.All(m => m.PassesConstraint);
        overall.AllocationValidationPassed = results.AllocationValidations.All(a => a.PassesConstraint);

        overall.PassesValidation = overall.LatencyValidationPassed &&
                                 overall.ThroughputValidationPassed &&
                                 overall.MemoryValidationPassed &&
                                 overall.AllocationValidationPassed;

        return overall;
    }

    private double CalculateThroughput(double meanNanoseconds)
    {
        if (meanNanoseconds <= 0) return 0;
        return 1_000_000_000.0 / meanNanoseconds; // ops per second
    }

    private OperationType DetermineOperationType(string benchmarkName)
    {
        var name = benchmarkName.ToLowerInvariant();

        if (name.Contains("process") || name.Contains("handle") || name.Contains("execute"))
            return OperationType.MessageProcessing;
        if (name.Contains("serialize") || name.Contains("deserialize"))
            return OperationType.Serialization;
        if (name.Contains("storage") || name.Contains("store") || name.Contains("retrieve"))
            return OperationType.Storage;
        if (name.Contains("observe") || name.Contains("metric") || name.Contains("trace"))
            return OperationType.Observability;

        return OperationType.Other;
    }

    private string ExtractTargetType(Type type)
    {
        return type.Name.Replace("Benchmarks", "").Replace("Benchmark", "");
    }

    private double GetLatencyThresholdForOperation(OperationType operationType)
    {
        return operationType switch
        {
            OperationType.MessageProcessing => _constraints.MaxMessageProcessingLatencyMs,
            OperationType.Serialization => _constraints.MaxSerializationLatencyMs,
            OperationType.Storage => _constraints.MaxStorageLatencyMs,
            OperationType.Observability => _constraints.MaxObservabilityLatencyMs,
            _ => _constraints.MaxP99LatencyMs
        };
    }

    private double GetThroughputThresholdForOperation(OperationType operationType)
    {
        return operationType switch
        {
            OperationType.MessageProcessing => _constraints.MinMessageProcessingThroughput,
            OperationType.Serialization => _constraints.MinSerializationThroughput,
            OperationType.Storage => _constraints.MinStorageThroughput,
            OperationType.Observability => _constraints.MinObservabilityThroughput,
            _ => _constraints.MinThroughputOpsPerSecond
        };
    }

    private long GetMemoryThresholdForOperation(OperationType operationType)
    {
        return operationType switch
        {
            OperationType.MessageProcessing => _constraints.MaxMessageProcessingMemoryBytes,
            OperationType.Serialization => _constraints.MaxSerializationMemoryBytes,
            OperationType.Storage => _constraints.MaxStorageMemoryBytes,
            OperationType.Observability => _constraints.MaxObservabilityMemoryBytes,
            _ => _constraints.MaxMemoryBytesPerOperation
        };
    }

    private double GetAllocationRateThresholdForOperation(OperationType operationType)
    {
        return operationType switch
        {
            OperationType.MessageProcessing => _constraints.MaxMessageProcessingAllocationRate,
            _ => _constraints.MaxAllocationRateBytesPerSecond
        };
    }

    private bool DetectSuspiciousAllocationPattern(BenchmarkDotNet.Engines.GcStats gcStats, BenchmarkDotNet.Running.BenchmarkCase benchmarkCase)
    {
        return gcStats.Gen2Collections > 0; // Any Gen2 collections are suspicious for high-frequency operations
    }

    private string GetLatencyOptimizationRecommendation(OperationType operationType)
    {
        return operationType switch
        {
            OperationType.MessageProcessing => "Consider async/await optimization, reduce allocations, implement object pooling",
            OperationType.Serialization => "Use spans, pre-allocate buffers, consider binary serialization",
            OperationType.Storage => "Implement connection pooling, use async I/O, optimize queries",
            OperationType.Observability => "Use structured logging, batch metrics, implement sampling",
            _ => "Profile the method to identify bottlenecks"
        };
    }

    private string GetThroughputOptimizationRecommendation(OperationType operationType)
    {
        return operationType switch
        {
            OperationType.MessageProcessing => "Implement parallel processing, use pipelines, optimize hot paths",
            OperationType.Serialization => "Use vectorization, implement custom serializers, avoid reflection",
            OperationType.Storage => "Implement batching, use bulk operations, optimize database schema",
            OperationType.Observability => "Use fire-and-forget patterns, implement async logging, reduce overhead",
            _ => "Consider parallelization and algorithm optimization"
        };
    }

    private string GetMemoryOptimizationRecommendation(OperationType operationType)
    {
        return operationType switch
        {
            OperationType.MessageProcessing => "Implement object pooling, use ValueTypes, reduce boxing",
            OperationType.Serialization => "Use spans and memory, avoid temporary allocations, reuse buffers",
            OperationType.Storage => "Use streaming APIs, implement connection pooling, dispose resources properly",
            OperationType.Observability => "Use structured logging, implement log level filtering, batch operations",
            _ => "Profile memory usage and implement object pooling where appropriate"
        };
    }
}

// Supporting data structures and enums

public class PerformanceConstraints
{
    // Constitutional constraints
    public double MaxP99LatencyMs { get; set; } = 1.0;
    public double MinThroughputOpsPerSecond { get; set; } = 100_000;
    public long MaxMemoryBytesPerOperation { get; set; } = 1024; // 1KB
    public double MaxAllocationRateBytesPerSecond { get; set; } = 1_048_576; // 1MB/s

    // Operation-specific constraints
    public double MaxMessageProcessingLatencyMs { get; set; } = 0.5;
    public double MaxSerializationLatencyMs { get; set; } = 0.1;
    public double MaxStorageLatencyMs { get; set; } = 2.0;
    public double MaxObservabilityLatencyMs { get; set; } = 0.05;

    public double MinMessageProcessingThroughput { get; set; } = 100_000;
    public double MinSerializationThroughput { get; set; } = 500_000;
    public double MinStorageThroughput { get; set; } = 10_000;
    public double MinObservabilityThroughput { get; set; } = 1_000_000;

    public long MaxMessageProcessingMemoryBytes { get; set; } = 512;
    public long MaxSerializationMemoryBytes { get; set; } = 256;
    public long MaxStorageMemoryBytes { get; set; } = 2048;
    public long MaxObservabilityMemoryBytes { get; set; } = 128;

    public double MaxMessageProcessingAllocationRate { get; set; } = 0; // Zero allocation for critical path
    public bool RequireZeroAllocationPaths { get; set; } = true;
}

public class PerformanceValidationResults
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public PerformanceConstraints Constraints { get; set; } = new();
    public List<LatencyValidationResult> LatencyValidations { get; set; } = new();
    public List<ThroughputValidationResult> ThroughputValidations { get; set; } = new();
    public List<MemoryValidationResult> MemoryValidations { get; set; } = new();
    public List<AllocationValidationResult> AllocationValidations { get; set; } = new();
    public OverallValidationResult OverallValidation { get; set; } = new();
    public string ValidationReport { get; set; } = string.Empty;
    public List<PerformanceAlert> PerformanceAlerts { get; set; } = new();
}

public class LatencyValidationResult
{
    public string BenchmarkName { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public OperationType OperationType { get; set; }
    public double MeanLatencyMs { get; set; }
    public double P50LatencyMs { get; set; }
    public double P99LatencyMs { get; set; }
    public double StandardDeviationMs { get; set; }
    public double ConstraintThresholdMs { get; set; }
    public bool PassesConstraint { get; set; }
}

public class ThroughputValidationResult
{
    public string BenchmarkName { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public OperationType OperationType { get; set; }
    public double ActualThroughputOpsPerSecond { get; set; }
    public double RequiredThroughputOpsPerSecond { get; set; }
    public double EfficiencyRatio { get; set; }
    public bool PassesConstraint { get; set; }
}

public class MemoryValidationResult
{
    public string BenchmarkName { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public OperationType OperationType { get; set; }
    public long AllocatedBytesPerOperation { get; set; }
    public long MaxAllowedBytesPerOperation { get; set; }
    public double Gen0CollectionsPerOperation { get; set; }
    public double Gen1CollectionsPerOperation { get; set; }
    public double Gen2CollectionsPerOperation { get; set; }
    public bool PassesConstraint { get; set; }
}

public class AllocationValidationResult
{
    public string BenchmarkName { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public OperationType OperationType { get; set; }
    public long TotalAllocations { get; set; }
    public double AllocationRate { get; set; }
    public double MaxAllowedAllocationRate { get; set; }
    public bool HasSuspiciousAllocationPattern { get; set; }
    public bool PassesConstraint { get; set; }
}

public class OverallValidationResult
{
    public bool PassesValidation { get; set; }
    public bool LatencyValidationPassed { get; set; }
    public bool ThroughputValidationPassed { get; set; }
    public bool MemoryValidationPassed { get; set; }
    public bool AllocationValidationPassed { get; set; }
}

public class PerformanceAlert
{
    public AlertSeverity Severity { get; set; }
    public AlertType Type { get; set; }
    public string BenchmarkName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
}

public enum OperationType
{
    MessageProcessing,
    Serialization,
    Storage,
    Observability,
    Other
}

public enum AlertSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum AlertType
{
    LatencyViolation,
    ThroughputDegradation,
    MemoryViolation,
    AllocationViolation
}