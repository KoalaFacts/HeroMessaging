using System.Linq;
using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class QueryProcessorTests : IDisposable
{
    private readonly ServiceCollection _services;
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<ILogger<QueryProcessor>> _loggerMock;

    public QueryProcessorTests()
    {
        _services = new ServiceCollection();
        _loggerMock = new Mock<ILogger<QueryProcessor>>();
        _serviceProvider = _services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }

    private QueryProcessor CreateProcessor()
    {
        var services = new ServiceCollection();

        // Copy existing registrations
        foreach (var service in _services)
        {
            ((IList<ServiceDescriptor>)services).Add(service);
        }

        var provider = services.BuildServiceProvider();
        return new QueryProcessor(provider, _loggerMock.Object);
    }

    #region Send - Success Cases

    [Fact]
    public async Task Send_WithValidQuery_ReturnsResult()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var query = new TestQuery();
        var expectedResult = "Query Result";

        handlerMock
            .Setup(h => h.HandleAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await processor.SendAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task Send_WithValidQuery_UpdatesMetrics()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var query = new TestQuery();

        handlerMock
            .Setup(h => h.HandleAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Result");

        // Act
        await processor.SendAsync(query, TestContext.Current.CancellationToken);

        // Assert
        var metrics = processor.GetMetrics();
        Assert.Equal(1, metrics.ProcessedCount);
        Assert.Equal(0, metrics.FailedCount);
    }

    [Fact]
    public async Task Send_WithMultipleQueries_ProcessesSequentially()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var query1 = new TestQuery();
        var query2 = new TestQuery();

        handlerMock
            .Setup(h => h.HandleAsync(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Result");

        // Act
        await processor.SendAsync(query1, TestContext.Current.CancellationToken);
        await processor.SendAsync(query2, TestContext.Current.CancellationToken);

        // Assert
        var metrics = processor.GetMetrics();
        Assert.Equal(2, metrics.ProcessedCount);
        handlerMock.Verify(h => h.HandleAsync(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Send_WithComplexResponse_ReturnsCorrectType()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQueryComplex, TestQueryResult>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var query = new TestQueryComplex();
        var expectedResult = new TestQueryResult { Id = 123, Name = "Test", IsActive = true };

        handlerMock
            .Setup(h => h.HandleAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await processor.SendAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expectedResult.Id, result.Id);
        Assert.Equal(expectedResult.Name, result.Name);
        Assert.Equal(expectedResult.IsActive, result.IsActive);
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task Send_WhenHandlerNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var processor = CreateProcessor();
        var query = new TestQuery();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await processor.SendAsync(query, TestContext.Current.CancellationToken));
        Assert.Contains("No handler found", exception.Message);
    }

    [Fact]
    public async Task Send_WhenHandlerThrowsException_PropagatesException()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var query = new TestQuery();
        var expectedException = new InvalidOperationException("Handler error");

        handlerMock
            .Setup(h => h.HandleAsync(query, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await processor.SendAsync(query, TestContext.Current.CancellationToken));
        Assert.Equal("Handler error", exception.Message);
    }

    [Fact]
    public async Task Send_WhenHandlerThrowsException_UpdatesFailureMetrics()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var query = new TestQuery();

        handlerMock
            .Setup(h => h.HandleAsync(query, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler error"));

        // Act
        try
        {
            await processor.SendAsync(query, TestContext.Current.CancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert
        var metrics = processor.GetMetrics();
        Assert.Equal(0, metrics.ProcessedCount);
        Assert.Equal(1, metrics.FailedCount);
    }

    [Fact]
    public async Task Send_WhenHandlerThrowsException_LogsError()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var query = new TestQuery();

        handlerMock
            .Setup(h => h.HandleAsync(query, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Handler error"));

        // Act
        try
        {
            await processor.SendAsync(query, TestContext.Current.CancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task Send_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var query = new TestQuery();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await processor.SendAsync(query, cts.Token, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Send_WithCancellationDuringProcessing_ThrowsOperationCanceledException()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var query = new TestQuery();
        var cts = new CancellationTokenSource();

        handlerMock
            .Setup(h => h.HandleAsync(query, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                cts.Cancel();
                await Task.Delay(100, cts.Token);
                return "Result";
            });

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await processor.SendAsync(query, cts.Token, TestContext.Current.CancellationToken));
    }

    #endregion

    #region Null Argument Validation

    [Fact]
    public async Task Send_WithNullQuery_ThrowsArgumentNullException()
    {
        // Arrange
        var processor = CreateProcessor();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await processor.SendAsync((IQuery<string>)null!));
    }

    #endregion

    #region Metrics

    [Fact]
    public void GetMetrics_WithNoQueries_ReturnsZeroMetrics()
    {
        // Arrange
        var processor = CreateProcessor();

        // Act
        var metrics = processor.GetMetrics();

        // Assert
        Assert.Equal(0, metrics.ProcessedCount);
        Assert.Equal(0, metrics.FailedCount);
        Assert.Equal(TimeSpan.Zero, metrics.AverageDuration);
        Assert.Equal(0, metrics.CacheHitRate);
    }

    [Fact]
    public async Task GetMetrics_AfterProcessing_CalculatesAverageDuration()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();
        var query = new TestQuery();

        handlerMock
            .Setup(h => h.HandleAsync(query, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(10);
                return "Result";
            });

        // Act
        await processor.SendAsync(query, TestContext.Current.CancellationToken);

        // Assert
        var metrics = processor.GetMetrics();
        Assert.True(metrics.AverageDuration > TimeSpan.Zero);
    }

    [Fact]
    public async Task GetMetrics_WithMultipleQueries_TracksAllMetrics()
    {
        // Arrange
        var handlerMock = new Mock<IQueryHandler<TestQuery, string>>();
        _services.AddSingleton(handlerMock.Object);
        var processor = CreateProcessor();

        handlerMock
            .Setup(h => h.HandleAsync(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Result");

        // Act
        await processor.SendAsync(new TestQuery(, TestContext.Current.CancellationToken));
        await processor.SendAsync(new TestQuery(, TestContext.Current.CancellationToken));

        try
        {
            handlerMock
                .Setup(h => h.HandleAsync(It.IsAny<TestQuery>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException());
            await processor.SendAsync(new TestQuery(, TestContext.Current.CancellationToken));
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert
        var metrics = processor.GetMetrics();
        Assert.Equal(2, metrics.ProcessedCount);
        Assert.Equal(1, metrics.FailedCount);
    }

    #endregion

    #region IsRunning

    [Fact]
    public void IsRunning_ReturnsTrue()
    {
        // Arrange
        var processor = CreateProcessor();

        // Act & Assert
        Assert.True(processor.IsRunning);
    }

    #endregion

    #region Test Helper Classes

    public class TestQuery : IQuery<string>
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }

    public class TestQueryComplex : IQuery<TestQueryResult>
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; } = [];
    }

    public class TestQueryResult
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    #endregion
}
