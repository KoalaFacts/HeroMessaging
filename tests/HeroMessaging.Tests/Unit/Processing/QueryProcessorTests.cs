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

    #endregion

    #region Test Queries

    public class TestQuery : IQuery<string>
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string Filter { get; set; } = string.Empty;
    }

    public class TestComplexQuery : IQuery<QueryResult>
    {
        public Guid MessageId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
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

    #endregion
}
