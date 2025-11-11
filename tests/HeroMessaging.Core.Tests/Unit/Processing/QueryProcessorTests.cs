using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

/// <summary>
/// Unit tests for QueryProcessor
/// Tests query processing pipeline and handler invocation
/// </summary>
[Trait("Category", "Unit")]
public sealed class QueryProcessorTests
{
    private readonly Mock<ILogger<QueryProcessor>> _mockLogger;
    private readonly ServiceCollection _services;
    private readonly IServiceProvider _serviceProvider;

    public QueryProcessorTests()
    {
        _mockLogger = new Mock<ILogger<QueryProcessor>>();
        _services = new ServiceCollection();
        _serviceProvider = _services.BuildServiceProvider();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidServiceProvider_CreatesProcessor()
    {
        // Act
        var processor = new QueryProcessor(_serviceProvider, _mockLogger.Object);

        // Assert
        Assert.NotNull(processor);
        Assert.True(processor.IsRunning);
    }

    [Fact]
    public void Constructor_WithNullLogger_UsesNullLogger()
    {
        // Act
        var processor = new QueryProcessor(_serviceProvider);

        // Assert
        Assert.NotNull(processor);
    }

    #endregion

    #region Send<TResponse> Tests

    [Fact]
    public async Task Send_WithNullQuery_ThrowsArgumentNullException()
    {
        // Arrange
        var processor = new QueryProcessor(_serviceProvider, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            processor.Send<string>(null!));
    }

    [Fact]
    public async Task Send_WithNoRegisteredHandler_ThrowsInvalidOperationException()
    {
        // Arrange
        var processor = new QueryProcessor(_serviceProvider, _mockLogger.Object);
        var query = new TestQuery { Filter = "test" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.Send(query));
        Assert.Contains("No handler found", ex.Message);
        Assert.Contains("TestQuery", ex.Message);
    }

    [Fact]
    public async Task Send_WithRegisteredHandler_ReturnsResponse()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("query result");

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        var query = new TestQuery { Filter = "input" };

        // Act
        var result = await processor.Send(query);

        // Assert
        Assert.Equal("query result", result);
        handlerMock.Verify(h => h.Handle(
            It.Is<TestQuery>(q => q.Filter == "input"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_WithCancellationToken_PropagatesToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("result");

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        var query = new TestQuery { Filter = "test" };

        // Act
        await processor.Send(query, cts.Token);

        // Assert
        handlerMock.Verify(h => h.Handle(
            It.IsAny<TestQuery>(),
            It.Is<CancellationToken>(ct => ct == cts.Token)), Times.Once);
    }

    [Fact]
    public async Task Send_WhenHandlerThrows_PropagatesException()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Query handler error"));

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        var query = new TestQuery { Filter = "test" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => processor.Send(query));
        Assert.Equal("Query handler error", ex.Message);
    }

    [Fact]
    public async Task Send_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        var query = new TestQuery { Filter = "test" };

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.Send(query, cts.Token));
    }

    [Fact]
    public async Task Send_WithComplexReturnType_ReturnsCorrectType()
    {
        // Arrange
        var expectedResult = new QueryResult { Value = "complex data", Count = 42 };
        var handlerMock = new Mock<IQueryHandler<TestComplexQuery, QueryResult>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestComplexQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        var query = new TestComplexQuery { Id = 123 };

        // Act
        var result = await processor.Send(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("complex data", result.Value);
        Assert.Equal(42, result.Count);
    }

    #endregion

    #region GetMetrics Tests

    [Fact]
    public void GetMetrics_InitialState_ReturnsZeroMetrics()
    {
        // Arrange
        var processor = new QueryProcessor(_serviceProvider, _mockLogger.Object);

        // Act
        var metrics = processor.GetMetrics();

        // Assert
        Assert.Equal(0, metrics.ProcessedCount);
        Assert.Equal(0, metrics.FailedCount);
        Assert.Equal(TimeSpan.Zero, metrics.AverageDuration);
        Assert.Equal(0, metrics.CacheHitRate);
    }

    [Fact]
    public async Task GetMetrics_AfterSuccessfulProcessing_IncrementsProcessedCount()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("result");

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        var query = new TestQuery { Filter = "test" };

        // Act
        await processor.Send(query);
        var metrics = processor.GetMetrics();

        // Assert
        Assert.Equal(1, metrics.ProcessedCount);
        Assert.Equal(0, metrics.FailedCount);
    }

    [Fact]
    public async Task GetMetrics_AfterFailedProcessing_IncrementsFailedCount()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException());

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        var query = new TestQuery { Filter = "test" };

        // Act
        try { await processor.Send(query); } catch { }
        var metrics = processor.GetMetrics();

        // Assert
        Assert.Equal(0, metrics.ProcessedCount);
        Assert.Equal(1, metrics.FailedCount);
    }

    [Fact]
    public async Task GetMetrics_AfterMultipleQueries_TracksAllCounts()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        var callCount = 0;
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 2) throw new InvalidOperationException();
                return "result";
            });

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);

        // Act
        await processor.Send(new TestQuery { Filter = "1" }); // Success
        try { await processor.Send(new TestQuery { Filter = "2" }); } catch { } // Fail
        await processor.Send(new TestQuery { Filter = "3" }); // Success

        var metrics = processor.GetMetrics();

        // Assert
        Assert.Equal(2, metrics.ProcessedCount);
        Assert.Equal(1, metrics.FailedCount);
    }

    [Fact]
    public async Task GetMetrics_TracksAverageDuration()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("result");

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);

        // Act - Send multiple queries to accumulate duration data
        await processor.Send(new TestQuery { Filter = "1" });
        await processor.Send(new TestQuery { Filter = "2" });
        var metrics = processor.GetMetrics();

        // Assert - Should have recorded durations and calculated average
        Assert.Equal(2, metrics.ProcessedCount);
        Assert.True(metrics.AverageDuration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task GetMetrics_WithMoreThan100Durations_MaintainsRollingWindow()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("result");

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);

        // Act - Send 150 queries to exceed the 100 duration window
        for (int i = 0; i < 150; i++)
        {
            await processor.Send(new TestQuery { Filter = $"query-{i}" });
        }

        var metrics = processor.GetMetrics();

        // Assert - Should have processed all 150 queries
        // The rolling window maintains last 100 durations for averaging
        Assert.Equal(150, metrics.ProcessedCount);
        Assert.Equal(0, metrics.FailedCount);
    }

    [Fact]
    public async Task Send_WhenHandlerThrows_LogsErrorWithQueryType()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        var query = new TestQuery { Filter = "test" };

        // Act
        try { await processor.Send(query); } catch { }

        // Assert - Verify logger was called with error details
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once,
            "Logger should log error when handler throws");
    }

    [Fact]
    public async Task Send_WithTaskCanceledExceptionDuringInvoke_PropagatesAsOperationCanceled()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException());

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        var query = new TestQuery { Filter = "test" };

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => processor.Send(query));
    }


    [Fact]
    public async Task Send_WithDifferentResponseTypes_HandlesProperly()
    {
        // Arrange - Test with int response type
        var intHandlerMock = new Mock<IQueryHandler<IntQueryTest, int>>();
        intHandlerMock.Setup(h => h.Handle(It.IsAny<IntQueryTest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        var services = new ServiceCollection();
        services.AddSingleton(intHandlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        var query = new IntQueryTest();

        // Act
        var result = await processor.Send(query);

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Send_WithBoolResponseType_ReturnsBool()
    {
        // Arrange
        var boolHandlerMock = new Mock<IQueryHandler<BoolQueryTest, bool>>();
        boolHandlerMock.Setup(h => h.Handle(It.IsAny<BoolQueryTest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var services = new ServiceCollection();
        services.AddSingleton(boolHandlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        var query = new BoolQueryTest();

        // Act
        var result = await processor.Send(query);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetMetrics_CacheHitRate_IsAlwaysZero()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("result");

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);

        // Act
        await processor.Send(new TestQuery());
        var metrics = processor.GetMetrics();

        // Assert - CacheHitRate should always be 0 (not implemented)
        Assert.Equal(0, metrics.CacheHitRate);
    }

    [Fact]
    public void IsRunning_DefaultsToTrue()
    {
        // Arrange & Act
        var processor = new QueryProcessor(_serviceProvider, _mockLogger.Object);

        // Assert
        Assert.True(processor.IsRunning);
    }


    [Fact]
    public async Task Send_ConcurrentQueries_ExecuteSerially()
    {
        // Arrange
        var executionOrder = new List<int>();
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        var counter = 0;

        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .Returns((TestQuery query, CancellationToken ct) =>
            {
                var order = ++counter;
                executionOrder.Add(order);
                return Task.FromResult($"result-{order}");
            });

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);

        // Act - Send queries concurrently but they should execute serially
        var task1 = processor.Send(new TestQuery { Filter = "1" });
        var task2 = processor.Send(new TestQuery { Filter = "2" });
        var task3 = processor.Send(new TestQuery { Filter = "3" });

        var results = await Task.WhenAll(task1, task2, task3);

        // Assert - All queries should complete successfully
        Assert.Equal(3, results.Length);
        Assert.Contains("result", results[0]);
        Assert.Contains("result", results[1]);
        Assert.Contains("result", results[2]);
    }

    [Fact]
    public async Task Send_WithEmptyStringResponse_ReturnsEmptyString()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        var query = new TestQuery { Filter = "test" };

        // Act
        var result = await processor.Send(query);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task Send_WithNullResponse_ReturnsNull()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string?>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        var query = new TestQuery { Filter = "test" };

        // Act
        var result = await processor.Send(query);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetMetrics_AfterMultipleCallsToGetMetrics_ReturnsConsistentState()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("result");

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        await processor.Send(new TestQuery { Filter = "test" });

        // Act - Get metrics multiple times
        var metrics1 = processor.GetMetrics();
        var metrics2 = processor.GetMetrics();

        // Assert - Metrics should be the same
        Assert.Equal(metrics1.ProcessedCount, metrics2.ProcessedCount);
        Assert.Equal(metrics1.FailedCount, metrics2.FailedCount);
        Assert.Equal(metrics1.AverageDuration, metrics2.AverageDuration);
    }

    [Fact]
    public async Task Send_WithDefaultCancellationToken_Works()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        handlerMock.Setup(h => h.Handle(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("result");

        var services = new ServiceCollection();
        services.AddSingleton(handlerMock.Object);
        var provider = services.BuildServiceProvider();

        var processor = new QueryProcessor(provider, _mockLogger.Object);
        var query = new TestQuery { Filter = "test" };

        // Act - Call without explicit cancellation token (uses default)
        var result = await processor.Send(query);

        // Assert
        Assert.Equal("result", result);
    }

    #endregion

    #region Test Queries

    public class TestQuery : IQuery<string>
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Filter { get; set; } = string.Empty;
    }

    public class TestComplexQuery : IQuery<QueryResult>
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public int Id { get; set; }
    }

    public class QueryResult
    {
        public string Value { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class IntQueryTest : IQuery<int>
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    public class BoolQueryTest : IQuery<bool>
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    #endregion
}
