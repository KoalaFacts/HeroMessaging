using HeroMessaging.Core.Processing.Decorators;

namespace HeroMessaging.Tests.Unit.Metrics;

public class InMemoryMetricsCollectorTests
{
    private readonly InMemoryMetricsCollector _collector = new();
    
    [Fact]
    public void IncrementCounter_ShouldIncreaseCounterValue()
    {
        // Act
        _collector.IncrementCounter("test.counter");
        _collector.IncrementCounter("test.counter");
        _collector.IncrementCounter("test.counter", 3);
        
        // Assert
        var snapshot = _collector.GetSnapshot();
        Assert.Equal(5L, snapshot["test.counter"]);
    }
    
    [Fact]
    public void IncrementCounter_WithMultipleCounters_ShouldTrackSeparately()
    {
        // Act
        _collector.IncrementCounter("counter1", 2);
        _collector.IncrementCounter("counter2", 3);
        _collector.IncrementCounter("counter1", 1);
        
        // Assert
        var snapshot = _collector.GetSnapshot();
        Assert.Equal(3L, snapshot["counter1"]);
        Assert.Equal(3L, snapshot["counter2"]);
    }
    
    [Fact]
    public void RecordDuration_ShouldCalculateStatistics()
    {
        // Act
        _collector.RecordDuration("operation.duration", TimeSpan.FromMilliseconds(100));
        _collector.RecordDuration("operation.duration", TimeSpan.FromMilliseconds(200));
        _collector.RecordDuration("operation.duration", TimeSpan.FromMilliseconds(300));
        
        // Assert
        var snapshot = _collector.GetSnapshot();
        Assert.Equal(3, snapshot["operation.duration.count"]);
        Assert.Equal(200.0, snapshot["operation.duration.avg_ms"]);
        Assert.Equal(300.0, snapshot["operation.duration.max_ms"]);
        Assert.Equal(100.0, snapshot["operation.duration.min_ms"]);
    }
    
    [Fact]
    public void RecordValue_ShouldStoreValues()
    {
        // Act
        _collector.RecordValue("metric.value", 10.5);
        _collector.RecordValue("metric.value", 20.5);
        _collector.RecordValue("metric.value", 30.5);
        
        // Assert
        // Note: Current implementation doesn't expose values in snapshot
        // This test verifies the method doesn't throw
        var snapshot = _collector.GetSnapshot();
        Assert.NotNull(snapshot);
    }
    
    [Fact]
    public void GetSnapshot_WithNoData_ShouldReturnEmptyDictionary()
    {
        // Act
        var snapshot = _collector.GetSnapshot();
        
        // Assert
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot);
    }
    
    [Fact]
    public async Task ConcurrentOperations_ShouldBeThreadSafe()
    {
        // Arrange
        var tasks = new List<Task>();
        
        // Act
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                _collector.IncrementCounter("concurrent.counter");
                _collector.RecordDuration("concurrent.duration", TimeSpan.FromMilliseconds(i));
            }));
        }
        
        await Task.WhenAll(tasks);
        
        // Assert
        var snapshot = _collector.GetSnapshot();
        Assert.Equal(100L, snapshot["concurrent.counter"]);
        Assert.Equal(100, snapshot["concurrent.duration.count"]);
    }
    
    [Fact]
    public void RecordDuration_WithSingleValue_ShouldCalculateCorrectStats()
    {
        // Act
        _collector.RecordDuration("single.duration", TimeSpan.FromMilliseconds(150));
        
        // Assert
        var snapshot = _collector.GetSnapshot();
        Assert.Equal(1, snapshot["single.duration.count"]);
        Assert.Equal(150.0, snapshot["single.duration.avg_ms"]);
        Assert.Equal(150.0, snapshot["single.duration.max_ms"]);
        Assert.Equal(150.0, snapshot["single.duration.min_ms"]);
    }
    
    [Fact]
    public void IncrementCounter_WithNegativeValue_ShouldDecrementCounter()
    {
        // Act
        _collector.IncrementCounter("test.counter", 10);
        _collector.IncrementCounter("test.counter", -3);
        
        // Assert
        var snapshot = _collector.GetSnapshot();
        Assert.Equal(7L, snapshot["test.counter"]);
    }
}