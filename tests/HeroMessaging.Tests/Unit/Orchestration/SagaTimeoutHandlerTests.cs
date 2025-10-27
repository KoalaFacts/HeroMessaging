using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration;

[Trait("Category", "Unit")]
public class SagaTimeoutHandlerTests
{
    private class TestSaga : SagaBase
    {
        public string? Data { get; set; }
    }

    [Fact]
    public async Task TimeoutHandler_FindsStaleSagas_AndMarksThemAsTimedOut()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var services = new ServiceCollection();
        services.AddSingleton<ISagaRepository<TestSaga>>(repository);
        var serviceProvider = services.BuildServiceProvider();

        var options = new SagaTimeoutOptions
        {
            CheckInterval = TimeSpan.FromMilliseconds(100),
            DefaultTimeout = TimeSpan.FromSeconds(1)
        };

        var logger = NullLogger<SagaTimeoutHandler<TestSaga>>.Instance;
        var handler = new SagaTimeoutHandler<TestSaga>(serviceProvider, options, logger);

        // Create a saga that's already old enough to timeout
        var staleSaga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Processing",
            Data = "test",
            UpdatedAt = DateTime.UtcNow.AddHours(-2), // 2 hours old
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            IsCompleted = false,
            Version = 0
        };

        await repository.SaveAsync(staleSaga);

        // Act - Start the handler and let it run one check cycle
        using var cts = new CancellationTokenSource();
        var handlerTask = handler.StartAsync(cts.Token);

        // Wait for timeout check to occur
        await Task.Delay(250);

        // Stop the handler
        cts.Cancel();
        try
        {
            await handlerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        var updatedSaga = await repository.FindAsync(staleSaga.CorrelationId);
        Assert.NotNull(updatedSaga);
        Assert.Equal("TimedOut", updatedSaga!.CurrentState);
        Assert.True(updatedSaga.IsCompleted);
        Assert.Equal(1, updatedSaga.Version); // Version incremented by update
    }

    [Fact]
    public async Task TimeoutHandler_IgnoresCompletedSagas()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var services = new ServiceCollection();
        services.AddSingleton<ISagaRepository<TestSaga>>(repository);
        var serviceProvider = services.BuildServiceProvider();

        var options = new SagaTimeoutOptions
        {
            CheckInterval = TimeSpan.FromMilliseconds(100),
            DefaultTimeout = TimeSpan.FromSeconds(1)
        };

        var logger = NullLogger<SagaTimeoutHandler<TestSaga>>.Instance;
        var handler = new SagaTimeoutHandler<TestSaga>(serviceProvider, options, logger);

        // Create a completed saga that's old
        var completedSaga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Completed",
            UpdatedAt = DateTime.UtcNow.AddHours(-2),
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            IsCompleted = true, // Already completed
            Version = 0
        };

        await repository.SaveAsync(completedSaga);

        // Act
        using var cts = new CancellationTokenSource();
        var handlerTask = handler.StartAsync(cts.Token);
        await Task.Delay(250);
        cts.Cancel();
        try
        {
            await handlerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - Saga should not be modified
        var saga = await repository.FindAsync(completedSaga.CorrelationId);
        Assert.NotNull(saga);
        Assert.Equal("Completed", saga!.CurrentState);
        Assert.True(saga.IsCompleted);
        Assert.Equal(0, saga.Version); // Version NOT incremented
    }

    [Fact]
    public async Task TimeoutHandler_IgnoresRecentSagas()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var services = new ServiceCollection();
        services.AddSingleton<ISagaRepository<TestSaga>>(repository);
        var serviceProvider = services.BuildServiceProvider();

        var options = new SagaTimeoutOptions
        {
            CheckInterval = TimeSpan.FromMilliseconds(100),
            DefaultTimeout = TimeSpan.FromHours(1) // 1 hour timeout
        };

        var logger = NullLogger<SagaTimeoutHandler<TestSaga>>.Instance;
        var handler = new SagaTimeoutHandler<TestSaga>(serviceProvider, options, logger);

        // Create a recent saga
        var recentSaga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Processing",
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5), // Only 5 minutes old
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            IsCompleted = false,
            Version = 0
        };

        await repository.SaveAsync(recentSaga);

        // Act
        using var cts = new CancellationTokenSource();
        var handlerTask = handler.StartAsync(cts.Token);
        await Task.Delay(250);
        cts.Cancel();
        try
        {
            await handlerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - Saga should not be modified
        var saga = await repository.FindAsync(recentSaga.CorrelationId);
        Assert.NotNull(saga);
        Assert.Equal("Processing", saga!.CurrentState);
        Assert.False(saga.IsCompleted);
        Assert.Equal(0, saga.Version); // Version NOT incremented
    }

    [Fact]
    public async Task TimeoutHandler_HandlesMultipleStaleSagas()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();
        var services = new ServiceCollection();
        services.AddSingleton<ISagaRepository<TestSaga>>(repository);
        var serviceProvider = services.BuildServiceProvider();

        var options = new SagaTimeoutOptions
        {
            CheckInterval = TimeSpan.FromMilliseconds(100),
            DefaultTimeout = TimeSpan.FromSeconds(1)
        };

        var logger = NullLogger<SagaTimeoutHandler<TestSaga>>.Instance;
        var handler = new SagaTimeoutHandler<TestSaga>(serviceProvider, options, logger);

        // Create multiple stale sagas
        var saga1 = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Processing",
            UpdatedAt = DateTime.UtcNow.AddHours(-2),
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            Version = 0
        };

        var saga2 = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "AwaitingPayment",
            UpdatedAt = DateTime.UtcNow.AddHours(-3),
            CreatedAt = DateTime.UtcNow.AddHours(-3),
            Version = 0
        };

        var saga3 = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "AwaitingInventory",
            UpdatedAt = DateTime.UtcNow.AddHours(-4),
            CreatedAt = DateTime.UtcNow.AddHours(-4),
            Version = 0
        };

        await repository.SaveAsync(saga1);
        await repository.SaveAsync(saga2);
        await repository.SaveAsync(saga3);

        // Act
        using var cts = new CancellationTokenSource();
        var handlerTask = handler.StartAsync(cts.Token);
        await Task.Delay(250);
        cts.Cancel();
        try
        {
            await handlerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - All three should be timed out
        var updated1 = await repository.FindAsync(saga1.CorrelationId);
        var updated2 = await repository.FindAsync(saga2.CorrelationId);
        var updated3 = await repository.FindAsync(saga3.CorrelationId);

        Assert.Equal("TimedOut", updated1!.CurrentState);
        Assert.Equal("TimedOut", updated2!.CurrentState);
        Assert.Equal("TimedOut", updated3!.CurrentState);

        Assert.True(updated1.IsCompleted);
        Assert.True(updated2.IsCompleted);
        Assert.True(updated3.IsCompleted);
    }

    [Fact]
    public void SagaTimeoutOptions_HasCorrectDefaults()
    {
        // Arrange & Act
        var options = new SagaTimeoutOptions();

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(1), options.CheckInterval);
        Assert.Equal(TimeSpan.FromHours(24), options.DefaultTimeout);
        Assert.True(options.Enabled);
    }

    [Fact]
    public async Task FindStaleAsync_FiltersCorrectly()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>();

        // Create sagas with different ages
        var freshSaga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Processing",
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        var staleSaga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Processing",
            UpdatedAt = DateTime.UtcNow.AddHours(-2),
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        };

        var completedOldSaga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Completed",
            UpdatedAt = DateTime.UtcNow.AddHours(-3),
            CreatedAt = DateTime.UtcNow.AddHours(-3),
            IsCompleted = true
        };

        await repository.SaveAsync(freshSaga);
        await repository.SaveAsync(staleSaga);
        await repository.SaveAsync(completedOldSaga);

        // Act
        var stale = await repository.FindStaleAsync(TimeSpan.FromHours(1));
        var staleList = stale.ToList();

        // Assert
        Assert.Single(staleList);
        Assert.Equal(staleSaga.CorrelationId, staleList[0].CorrelationId);
    }
}
