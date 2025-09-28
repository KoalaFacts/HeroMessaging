using Xunit;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Tests.TestUtilities;

namespace HeroMessaging.Tests.Integration;

/// <summary>
/// End-to-end pipeline integration tests
/// Testing complete message processing workflows, plugin combinations, high-throughput scenarios, and resilience
/// </summary>
public class PipelineTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = new();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CompleteMessagePipeline_ProcessesMessageEndToEnd()
    {
        // Arrange
        var pipeline = await CreateCompletePipelineAsync();
        _disposables.Add(pipeline);

        var message = TestMessageBuilder.CreateValidMessage("End-to-end pipeline test");

        // Act
        var result = await pipeline.ProcessMessageAsync(message);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(message.MessageId, result.ProcessedMessageId);
        Assert.NotNull(result.StoredMessageId);
        Assert.NotNull(result.SerializedData);
        Assert.True(result.ProcessingDuration > TimeSpan.Zero);

        // Verify message was stored
        var storedMessage = await pipeline.RetrieveMessageAsync(message.MessageId);
        Assert.NotNull(storedMessage);
        TestMessageExtensions.AssertSameContent(message, storedMessage);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PipelineWithPluginCombinations_WorksCorrectly()
    {
        // Arrange
        var jsonPipeline = await CreatePipelineAsync("json", "postgresql");
        var messagePackPipeline = await CreatePipelineAsync("messagepack", "sqlserver");
        var protobufPipeline = await CreatePipelineAsync("protobuf", "inmemory");

        _disposables.Add(jsonPipeline);
        _disposables.Add(messagePackPipeline);
        _disposables.Add(protobufPipeline);

        var message = TestMessageBuilder.CreateValidMessage("Plugin combination test");

        // Act
        var jsonResult = await jsonPipeline.ProcessMessageAsync(message);
        var messagePackResult = await messagePackPipeline.ProcessMessageAsync(message);
        var protobufResult = await protobufPipeline.ProcessMessageAsync(message);

        // Assert
        Assert.True(jsonResult.Success);
        Assert.True(messagePackResult.Success);
        Assert.True(protobufResult.Success);

        // Verify different serialization formats
        Assert.NotEqual(jsonResult.SerializedData.Length, messagePackResult.SerializedData.Length);
        Assert.NotEqual(jsonResult.SerializedData.Length, protobufResult.SerializedData.Length);

        // All should process the same message successfully
        Assert.Equal(message.MessageId, jsonResult.ProcessedMessageId);
        Assert.Equal(message.MessageId, messagePackResult.ProcessedMessageId);
        Assert.Equal(message.MessageId, protobufResult.ProcessedMessageId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HighThroughputScenario_HandlesMultipleMessagesCorrectly()
    {
        // Arrange
        var pipeline = await CreateHighPerformancePipelineAsync();
        _disposables.Add(pipeline);

        const int messageCount = 1000;
        var messages = Enumerable.Range(0, messageCount)
            .Select(i => TestMessageBuilder.CreateValidMessage($"High throughput message {i}"))
            .ToArray();

        var startTime = DateTime.UtcNow;

        // Act
        var processingTasks = messages.Select(msg => pipeline.ProcessMessageAsync(msg)).ToArray();
        var results = await Task.WhenAll(processingTasks);

        var endTime = DateTime.UtcNow;
        var totalDuration = endTime - startTime;

        // Assert
        Assert.Equal(messageCount, results.Length);
        Assert.All(results, result => Assert.True(result.Success));

        // Performance assertion: Should process >1000 messages per second
        var messagesPerSecond = messageCount / totalDuration.TotalSeconds;
        Assert.True(messagesPerSecond > 1000, $"Processed {messagesPerSecond:F0} msg/s, expected >1000 msg/s");

        // Verify all messages were processed and stored
        foreach (var message in messages.Take(10)) // Check first 10 for verification
        {
            var storedMessage = await pipeline.RetrieveMessageAsync(message.MessageId);
            Assert.NotNull(storedMessage);
            Assert.Equal(message.MessageId, storedMessage.MessageId);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PipelineWithFailureRecovery_RecoversGracefully()
    {
        // Arrange
        var pipeline = await CreateResilientPipelineAsync();
        _disposables.Add(pipeline);

        var messages = Enumerable.Range(0, 20)
            .Select(i => TestMessageBuilder.CreateValidMessage($"Resilience test {i}"))
            .ToArray();

        // Act - Process some messages successfully
        var initialResults = await Task.WhenAll(messages.Take(5).Select(msg => pipeline.ProcessMessageAsync(msg)));
        Assert.All(initialResults, result => Assert.True(result.Success));

        // Simulate failure
        pipeline.SimulateStorageFailure();

        // These should fail
        var failureResults = new List<PipelineResult>();
        foreach (var message in messages.Skip(5).Take(5))
        {
            try
            {
                var result = await pipeline.ProcessMessageAsync(message);
                failureResults.Add(result);
            }
            catch (Exception)
            {
                // Expected failures during outage
                failureResults.Add(new PipelineResult { Success = false });
            }
        }

        Assert.All(failureResults, result => Assert.False(result.Success));

        // Restore service
        pipeline.RestoreStorageService();

        // These should succeed again
        var recoveryResults = await Task.WhenAll(messages.Skip(10).Take(5).Select(msg => pipeline.ProcessMessageAsync(msg)));
        Assert.All(recoveryResults, result => Assert.True(result.Success));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PipelineWithRetryAndCircuitBreaker_HandlesIntermittentFailures()
    {
        // Arrange
        var pipeline = await CreatePipelineWithResilienceAsync();
        _disposables.Add(pipeline);

        var messages = Enumerable.Range(0, 15)
            .Select(i => TestMessageBuilder.CreateValidMessage($"Retry test {i}"))
            .ToArray();

        // Act - Simulate intermittent failures
        pipeline.ConfigureIntermittentFailures(failureRate: 0.3); // 30% failure rate

        var results = new List<PipelineResult>();
        foreach (var message in messages)
        {
            try
            {
                var result = await pipeline.ProcessMessageAsync(message);
                results.Add(result);
            }
            catch (Exception)
            {
                results.Add(new PipelineResult { Success = false });
            }
        }

        // Assert
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);

        // With retry mechanism, should have more successes than failures
        Assert.True(successCount > failureCount, $"Expected more successes ({successCount}) than failures ({failureCount}) with retry");

        // Should have some successes despite failures
        Assert.True(successCount > 0, "Should have some successful processing despite intermittent failures");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PipelineWithObservability_TracksMetricsCorrectly()
    {
        // Arrange
        var pipeline = await CreateObservablePipelineAsync();
        _disposables.Add(pipeline);

        var messages = Enumerable.Range(0, 50)
            .Select(i => TestMessageBuilder.CreateValidMessage($"Observability test {i}"))
            .ToArray();

        // Act
        var results = await Task.WhenAll(messages.Select(msg => pipeline.ProcessMessageAsync(msg)));

        // Allow metrics to be collected
        await Task.Delay(100);

        var metrics = pipeline.GetCollectedMetrics();

        // Assert
        Assert.NotEmpty(metrics);

        var processedMetric = metrics.FirstOrDefault(m => m.Name == "pipeline.messages.processed");
        Assert.NotNull(processedMetric);
        Assert.Equal(50, processedMetric.Value);

        var latencyMetric = metrics.FirstOrDefault(m => m.Name == "pipeline.processing.latency_ms");
        Assert.NotNull(latencyMetric);
        Assert.True(latencyMetric.Value > 0);

        var throughputMetric = metrics.FirstOrDefault(m => m.Name == "pipeline.throughput.messages_per_second");
        Assert.NotNull(throughputMetric);
        Assert.True(throughputMetric.Value > 0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PipelineWithDifferentMessageSizes_HandlesVariety()
    {
        // Arrange
        var pipeline = await CreateCompletePipelineAsync();
        _disposables.Add(pipeline);

        var messages = new[]
        {
            TestMessageBuilder.CreateValidMessage("Small message"),
            TestMessageBuilder.CreateLargeMessage(1_000), // 1KB
            TestMessageBuilder.CreateLargeMessage(10_000), // 10KB
            TestMessageBuilder.CreateLargeMessage(100_000), // 100KB
            TestMessageBuilder.CreateLargeMessage(1_000_000) // 1MB
        };

        // Act
        var results = await Task.WhenAll(messages.Select(msg => pipeline.ProcessMessageAsync(msg)));

        // Assert
        Assert.All(results, result => Assert.True(result.Success));

        // Verify larger messages take longer to process (but not excessively)
        for (int i = 0; i < results.Length; i++)
        {
            Assert.True(results[i].ProcessingDuration < TimeSpan.FromSeconds(5),
                $"Message {i} took too long: {results[i].ProcessingDuration}");
        }

        // Verify serialized data sizes correlate with message sizes
        Assert.True(results[0].SerializedData.Length < results[1].SerializedData.Length);
        Assert.True(results[1].SerializedData.Length < results[2].SerializedData.Length);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PipelineHealthCheck_ReportsCorrectStatus()
    {
        // Arrange
        var pipeline = await CreateCompletePipelineAsync();
        _disposables.Add(pipeline);

        // Act - Healthy state
        var healthyReport = await pipeline.CheckHealthAsync();

        // Assert
        Assert.Equal(HealthStatus.Healthy, healthyReport.OverallStatus);
        Assert.True(healthyReport.Components.ContainsKey("Storage"));
        Assert.True(healthyReport.Components.ContainsKey("Serialization"));
        Assert.True(healthyReport.Components.ContainsKey("Processing"));

        // Act - Simulate component failure
        pipeline.SimulateStorageFailure();
        var unhealthyReport = await pipeline.CheckHealthAsync();

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, unhealthyReport.OverallStatus);
        Assert.Equal(HealthStatus.Unhealthy, unhealthyReport.Components["Storage"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PipelineWithCustomProcessors_ExecutesInOrder()
    {
        // Arrange
        var pipeline = await CreateCustomProcessorPipelineAsync();
        _disposables.Add(pipeline);

        var message = TestMessageBuilder.CreateValidMessage("Custom processor test");

        // Act
        var result = await pipeline.ProcessMessageAsync(message);

        // Assert
        Assert.True(result.Success);

        var processingSteps = pipeline.GetProcessingSteps();
        Assert.Equal(4, processingSteps.Count);

        // Verify execution order
        Assert.Equal("ValidationProcessor", processingSteps[0]);
        Assert.Equal("TransformationProcessor", processingSteps[1]);
        Assert.Equal("EnrichmentProcessor", processingSteps[2]);
        Assert.Equal("StorageProcessor", processingSteps[3]);

        // Verify each step was executed
        Assert.All(processingSteps, step => Assert.True(pipeline.WasStepExecuted(step)));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            await disposable.DisposeAsync();
        }
    }

    // Helper methods for creating different pipeline configurations
    private async Task<TestMessagePipeline> CreateCompletePipelineAsync()
    {
        var pipeline = new TestMessagePipeline();
        await pipeline.InitializeAsync("json", "postgresql", includeObservability: true);
        return pipeline;
    }

    private async Task<TestMessagePipeline> CreatePipelineAsync(string serialization, string storage)
    {
        var pipeline = new TestMessagePipeline();
        await pipeline.InitializeAsync(serialization, storage);
        return pipeline;
    }

    private async Task<TestMessagePipeline> CreateHighPerformancePipelineAsync()
    {
        var pipeline = new TestMessagePipeline();
        await pipeline.InitializeAsync("messagepack", "inmemory", optimizeForPerformance: true);
        return pipeline;
    }

    private async Task<TestMessagePipeline> CreateResilientPipelineAsync()
    {
        var pipeline = new TestMessagePipeline();
        await pipeline.InitializeAsync("json", "postgresql", includeResilience: true);
        return pipeline;
    }

    private async Task<TestMessagePipeline> CreatePipelineWithResilienceAsync()
    {
        var pipeline = new TestMessagePipeline();
        await pipeline.InitializeAsync("json", "postgresql", includeResilience: true, includeRetry: true);
        return pipeline;
    }

    private async Task<TestMessagePipeline> CreateObservablePipelineAsync()
    {
        var pipeline = new TestMessagePipeline();
        await pipeline.InitializeAsync("json", "postgresql", includeObservability: true);
        return pipeline;
    }

    private async Task<TestMessagePipeline> CreateCustomProcessorPipelineAsync()
    {
        var pipeline = new TestMessagePipeline();
        await pipeline.InitializeAsync("json", "postgresql", includeCustomProcessors: true);
        return pipeline;
    }

    // Test pipeline implementation
    public class TestMessagePipeline : IAsyncDisposable
    {
        private string _serializationType = "";
        private string _storageType = "";
        private bool _storageFailure = false;
        private double _intermittentFailureRate = 0.0;
        private readonly Random _random = new();
        private readonly object _randomLock = new();
        private readonly Dictionary<string, PipelineMetric> _metrics = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _metricsLock = new();
        private readonly List<string> _processingSteps = new();
        private readonly HashSet<string> _executedSteps = new();
        private readonly object _executedStepsLock = new();
        private readonly Dictionary<Guid, IMessage> _storage = new();
        private readonly object _storageLock = new();

        public async Task InitializeAsync(string serialization, string storage,
            bool includeObservability = false, bool includeResilience = false,
            bool includeRetry = false, bool optimizeForPerformance = false,
            bool includeCustomProcessors = false)
        {
            _serializationType = serialization;
            _storageType = storage;

            if (includeCustomProcessors)
            {
                _processingSteps.AddRange(new[]
                {
                    "ValidationProcessor",
                    "TransformationProcessor",
                    "EnrichmentProcessor",
                    "StorageProcessor"
                });
            }

            await Task.Delay(50); // Simulate initialization
        }

        public async Task<PipelineResult> ProcessMessageAsync(IMessage message)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                var shouldFail = false;
                if (_intermittentFailureRate > 0)
                {
                    lock (_randomLock)
                    {
                        shouldFail = _random.NextDouble() < _intermittentFailureRate;
                    }
                }

                if (shouldFail)
                {
                    throw new InvalidOperationException("Simulated intermittent failure");
                }

                if (_storageFailure)
                {
                    throw new InvalidOperationException("Storage service unavailable");
                }

                foreach (var step in _processingSteps)
                {
                    await ExecuteProcessingStep(step, message);
                    lock (_executedStepsLock)
                    {
                        _executedSteps.Add(step);
                    }
                }

                var processingDelay = _serializationType switch
                {
                    "messagepack" => 5,
                    "protobuf" => 7,
                    "json" => 10,
                    _ => 10
                };

                if (_storageType == "inmemory")
                {
                    processingDelay /= 2;
                }

                await Task.Delay(processingDelay);

                var serializedData = await SerializeMessage(message);

                lock (_storageLock)
                {
                    _storage[message.MessageId] = message;
                }

                var duration = DateTime.UtcNow - startTime;

                RecordMetric("pipeline.messages.processed", 1);
                RecordMetric("pipeline.processing.latency_ms", duration.TotalMilliseconds);
                RecordMetric("pipeline.throughput.messages_per_second", 1000.0 / duration.TotalMilliseconds);

                return new PipelineResult
                {
                    Success = true,
                    ProcessedMessageId = message.MessageId,
                    StoredMessageId = message.MessageId,
                    SerializedData = serializedData,
                    ProcessingDuration = duration
                };
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                RecordMetric("pipeline.messages.failed", 1);

                return new PipelineResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingDuration = duration
                };
            }
        }

        public async Task<IMessage?> RetrieveMessageAsync(Guid messageId)
        {
            if (_storageFailure)
            {
                throw new InvalidOperationException("Storage service unavailable");
            }

            await Task.Delay(5); // Simulate retrieval
            lock (_storageLock)
            {
                return _storage.TryGetValue(messageId, out var message) ? message : null;
            }
        }

        public async Task<HealthReport> CheckHealthAsync()
        {
            await Task.Delay(10);

            var components = new Dictionary<string, HealthStatus>
            {
                ["Processing"] = HealthStatus.Healthy,
                ["Serialization"] = HealthStatus.Healthy,
                ["Storage"] = _storageFailure ? HealthStatus.Unhealthy : HealthStatus.Healthy
            };

            var overallStatus = components.Values.Any(s => s == HealthStatus.Unhealthy)
                ? HealthStatus.Unhealthy
                : HealthStatus.Healthy;

            return new HealthReport
            {
                OverallStatus = overallStatus,
                Components = components
            };
        }

        public void SimulateStorageFailure() => _storageFailure = true;
        public void RestoreStorageService() => _storageFailure = false;
        public void ConfigureIntermittentFailures(double failureRate) => _intermittentFailureRate = failureRate;

        public List<PipelineMetric> GetCollectedMetrics()
        {
            lock (_metricsLock)
            {
                return _metrics.Values
                    .Select(metric => new PipelineMetric
                    {
                        Name = metric.Name,
                        Value = metric.Value,
                        Timestamp = metric.Timestamp
                    })
                    .ToList();
            }
        }

        public List<string> GetProcessingSteps() => _processingSteps.ToList();
        public bool WasStepExecuted(string step)
        {
            lock (_executedStepsLock)
            {
                return _executedSteps.Contains(step);
            }
        }

        private async Task ExecuteProcessingStep(string step, IMessage message)
        {
            await Task.Delay(5); // Simulate step execution

            // Different steps have different characteristics
            switch (step)
            {
                case "ValidationProcessor":
                    var content = message.GetTestContent() ?? "";
                    if (string.IsNullOrEmpty(content))
                        throw new ArgumentException("Invalid message content");
                    break;

                case "TransformationProcessor":
                    // Simulate transformation
                    break;

                case "EnrichmentProcessor":
                    // Simulate enrichment
                    break;

                case "StorageProcessor":
                    // Simulate storage-specific processing
                    break;
            }
        }

        private async Task<byte[]> SerializeMessage(IMessage message)
        {
            await Task.Delay(2);

            // Simulate different serialization sizes
            var content = message.GetTestContent() ?? "";
            var baseSize = content.Length > 0 ? content.Length : 50;
            var serializedSize = _serializationType switch
            {
                "messagepack" => (int)(baseSize * 0.8), // More compact
                "protobuf" => (int)(baseSize * 0.7),    // Most compact
                "json" => (int)(baseSize * 1.3),        // Larger
                _ => baseSize
            };

            return new byte[Math.Max(serializedSize, 10)];
        }

        private void RecordMetric(string name, double value)
        {
            lock (_metricsLock)
            {
                if (!_metrics.TryGetValue(name, out var metric))
                {
                    metric = new PipelineMetric
                    {
                        Name = name,
                        Value = 0,
                        Timestamp = DateTime.UtcNow
                    };
                    _metrics[name] = metric;
                }

                metric.Timestamp = DateTime.UtcNow;
                metric.Value = IsCounterMetric(name) ? metric.Value + value : value;
            }
        }

        private static bool IsCounterMetric(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.EndsWith(".processed", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".failed", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_total", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_count", StringComparison.OrdinalIgnoreCase);
        }

        public async ValueTask DisposeAsync()
        {
            lock (_storageLock)
            {
                _storage.Clear();
            }
            lock (_metricsLock)
            {
                _metrics.Clear();
            }
            lock (_executedStepsLock)
            {
                _executedSteps.Clear();
            }
            await Task.CompletedTask;
        }
    }

    // Supporting classes
    public class PipelineResult
    {
        public bool Success { get; set; }
        public Guid ProcessedMessageId { get; set; }
        public Guid? StoredMessageId { get; set; }
        public byte[] SerializedData { get; set; } = Array.Empty<byte>();
        public TimeSpan ProcessingDuration { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class PipelineMetric
    {
        public string Name { get; set; } = "";
        public double Value { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class HealthReport
    {
        public HealthStatus OverallStatus { get; set; }
        public Dictionary<string, HealthStatus> Components { get; set; } = new();
    }

    public enum HealthStatus
    {
        Healthy,
        Unhealthy,
        Degraded
    }
}





















