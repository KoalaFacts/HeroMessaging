using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
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
        var repository = new InMemorySagaRepository<TestSaga>(TimeProvider.System);
        var services = new ServiceCollection();
        services.AddSingleton<ISagaRepository<TestSaga>>(repository);
        var serviceProvider = services.BuildServiceProvider();

        var options = new SagaTimeoutOptions
        {
            CheckInterval = TimeSpan.FromMilliseconds(50), // Faster checks for testing
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

        // Poll for the timeout to be processed (with timeout)
        var pollTimeout = DateTime.UtcNow.AddSeconds(5);
        TestSaga? updatedSaga = null;
        while (DateTime.UtcNow < pollTimeout)
        {
            updatedSaga = await repository.FindAsync(staleSaga.CorrelationId);
            if (updatedSaga?.CurrentState == "TimedOut")
            {
                break;
            }
            await Task.Delay(50);
        }

        // Stop the handler properly
        await handler.StopAsync(cts.Token);
        cts.Cancel();
        try
        {
            await handlerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        finally
        {
            handler.Dispose();
        }

        // Assert
        Assert.NotNull(updatedSaga);
        Assert.Equal("TimedOut", updatedSaga!.CurrentState);
        Assert.True(updatedSaga.IsCompleted);
        Assert.Equal(1, updatedSaga.Version); // Version incremented by update
    }

    [Fact]
    public async Task TimeoutHandler_IgnoresCompletedSagas()
    {
        // Arrange
        var repository = new InMemorySagaRepository<TestSaga>(TimeProvider.System);
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
        await handler.StopAsync(cts.Token);
        cts.Cancel();
        try
        {
            await handlerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        finally
        {
            handler.Dispose();
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
        var repository = new InMemorySagaRepository<TestSaga>(TimeProvider.System);
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
        await handler.StopAsync(cts.Token);
        cts.Cancel();
        try
        {
            await handlerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        finally
        {
            handler.Dispose();
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
        var repository = new InMemorySagaRepository<TestSaga>(TimeProvider.System);
        var services = new ServiceCollection();
        services.AddSingleton<ISagaRepository<TestSaga>>(repository);
        var serviceProvider = services.BuildServiceProvider();

        var options = new SagaTimeoutOptions
        {
            CheckInterval = TimeSpan.FromMilliseconds(50), // Faster checks for testing
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

        // Poll for all sagas to be timed out (with timeout)
        var pollTimeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < pollTimeout)
        {
            var updated1 = await repository.FindAsync(saga1.CorrelationId);
            var updated2 = await repository.FindAsync(saga2.CorrelationId);
            var updated3 = await repository.FindAsync(saga3.CorrelationId);

            if (updated1?.CurrentState == "TimedOut" &&
                updated2?.CurrentState == "TimedOut" &&
                updated3?.CurrentState == "TimedOut")
            {
                break;
            }
            await Task.Delay(50);
        }

        await handler.StopAsync(cts.Token);
        cts.Cancel();
        try
        {
            await handlerTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        finally
        {
            handler.Dispose();
        }

        // Assert - All three should be timed out
        var final1 = await repository.FindAsync(saga1.CorrelationId);
        var final2 = await repository.FindAsync(saga2.CorrelationId);
        var final3 = await repository.FindAsync(saga3.CorrelationId);

        Assert.Equal("TimedOut", final1!.CurrentState);
        Assert.Equal("TimedOut", final2!.CurrentState);
        Assert.Equal("TimedOut", final3!.CurrentState);

        Assert.True(final1.IsCompleted);
        Assert.True(final2.IsCompleted);
        Assert.True(final3.IsCompleted);
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
        // Arrange - Use FakeTimeProvider to control time
        var fakeTime = new FakeTimeProvider();
        fakeTime.SetUtcNow(DateTimeOffset.Parse("2025-10-27T10:00:00Z")); // Start at 10:00
        var repository = new InMemorySagaRepository<TestSaga>(fakeTime);

        // Create stale saga at 10:00 (will be 2 hours old)
        var staleSaga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Processing"
        };
        await repository.SaveAsync(staleSaga);

        // Create completed old saga at 10:00 (will be old but completed, should be filtered out)
        var completedOldSaga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Completed",
            IsCompleted = true
        };
        await repository.SaveAsync(completedOldSaga);

        // Advance time to 12:00 (2 hours later)
        fakeTime.Advance(TimeSpan.FromHours(2));

        // Create fresh saga at 12:00 (current time, only 5 minutes will pass)
        var freshSaga = new TestSaga
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Processing"
        };
        await repository.SaveAsync(freshSaga);

        // Advance time by 5 more minutes to 12:05
        fakeTime.Advance(TimeSpan.FromMinutes(5));

        // Act - Find sagas older than 1 hour
        // At 12:05: staleSaga is 2h5min old, freshSaga is 5min old, completedOldSaga is completed
        var stale = await repository.FindStaleAsync(TimeSpan.FromHours(1));
        var staleList = stale.ToList();

        // Assert - Only staleSaga should be stale (>1 hour old and not completed)
        Assert.Single(staleList);
        Assert.Equal(staleSaga.CorrelationId, staleList[0].CorrelationId);
    }
}
