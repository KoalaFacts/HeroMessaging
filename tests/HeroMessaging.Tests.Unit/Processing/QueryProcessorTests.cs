using HeroMessaging.Abstractions.Handlers;
using HeroMessaging.Abstractions.Queries;
using HeroMessaging.Core.Processing;
using HeroMessaging.Tests.Unit.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace HeroMessaging.Tests.Unit.Processing;

public class QueryProcessorTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly QueryProcessor _processor;
    private readonly Mock<ILogger<QueryProcessor>> _loggerMock;

    public QueryProcessorTests()
    {
        _loggerMock = new Mock<ILogger<QueryProcessor>>();
        var services = new ServiceCollection();
        
        // Register test handlers
        services.AddSingleton<IQueryHandler<GetUserQuery, UserDto>, GetUserQueryHandler>();
        services.AddSingleton<IQueryHandler<GetItemsQuery, List<string>>, GetItemsQueryHandler>();
        services.AddSingleton<IQueryHandler<FailingQuery, string>, FailingQueryHandler>();
        services.AddSingleton<IQueryHandler<SlowQuery, string>, SlowQueryHandler>();
        
        _serviceProvider = services.BuildServiceProvider();
        _processor = new QueryProcessor(_serviceProvider, _loggerMock.Object);
    }

    [Fact]
    public async Task SendAsync_WithValidQuery_ShouldReturnResult()
    {
        // Arrange
        var query = new GetUserQuery { UserId = "123" };

        // Act
        var result = await _processor.Send(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("123", result.Id);
        Assert.Equal("User 123", result.Name);
    }

    [Fact]
    public async Task SendAsync_WithListQuery_ShouldReturnCollection()
    {
        // Arrange
        var query = new GetItemsQuery { Count = 3 };

        // Act
        var result = await _processor.Send(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal("Item 0", result[0]);
        Assert.Equal("Item 1", result[1]);
        Assert.Equal("Item 2", result[2]);
    }

    [Fact]
    public async Task SendAsync_WithNullQuery_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await _processor.Send((GetUserQuery)null!));
    }

    [Fact]
    public async Task SendAsync_WithNoHandler_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var query = new UnhandledQuery();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _processor.Send(query));
        Assert.Contains("No handler found", exception.Message);
    }

    [Fact]
    public async Task SendAsync_WhenHandlerThrows_ShouldPropagateException()
    {
        // Arrange
        var query = new FailingQuery { ShouldFail = true };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _processor.Send(query));
        Assert.Equal("Query failed", exception.Message);
    }

    [Fact]
    public async Task SendAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        var query = new SlowQuery { DelayMs = 5000 };
        var cts = new CancellationTokenSource(100);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await _processor.Send(query, cts.Token));
    }

    [Fact]
    public async Task SendAsync_MultipleQueries_ShouldProcessSequentially()
    {
        // Arrange
        var queries = Enumerable.Range(1, 10)
            .Select(i => new GetUserQuery { UserId = i.ToString() })
            .ToList();

        // Act
        var results = await Task.WhenAll(
            queries.Select(q => _processor.Send(q))
        );

        // Assert
        Assert.Equal(10, results.Length);
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal((i + 1).ToString(), results[i].Id);
        }
    }

    [Fact]
    public async Task GetMetrics_ShouldReturnCorrectStatistics()
    {
        // Arrange & Act
        await _processor.Send(new GetUserQuery { UserId = "1" });
        await _processor.Send(new GetItemsQuery { Count = 5 });
        try 
        { 
            await _processor.Send(new FailingQuery { ShouldFail = true }); 
        } 
        catch { }

        var metrics = _processor.GetMetrics();

        // Assert
        Assert.True(metrics.ProcessedCount >= 2);
        Assert.True(metrics.FailedCount >= 1);
        Assert.True(metrics.AverageDuration >= TimeSpan.Zero);
        Assert.Equal(0, metrics.CacheHitRate); // No caching implemented
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }
}