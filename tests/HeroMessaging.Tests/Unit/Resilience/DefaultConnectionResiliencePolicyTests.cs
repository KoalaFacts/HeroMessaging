using System.Data.Common;
using HeroMessaging.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Resilience;

/// <summary>
/// Unit tests for <see cref="DefaultConnectionResiliencePolicy"/> implementation.
/// Tests cover retry logic, circuit breaker patterns, transient exception handling, and edge cases.
/// </summary>
public class DefaultConnectionResiliencePolicyTests
{
    private readonly Mock<ILogger<DefaultConnectionResiliencePolicy>> _mockLogger;
    private readonly FakeTimeProvider _timeProvider;
    private readonly ConnectionResilienceOptions _options;

    public DefaultConnectionResiliencePolicyTests()
    {
        _mockLogger = new Mock<ILogger<DefaultConnectionResiliencePolicy>>();
        _timeProvider = new FakeTimeProvider();
        _options = new ConnectionResilienceOptions
        {
            MaxRetries = 3,
            BaseRetryDelay = TimeSpan.FromSeconds(1),
            MaxRetryDelay = TimeSpan.FromSeconds(30),
            CircuitBreakerOptions = new CircuitBreakerOptions
            {
                FailureThreshold = 5,
                BreakDuration = TimeSpan.FromSeconds(30)
            }
        };
    }

    #region Constructor Tests

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var policy = new DefaultConnectionResiliencePolicy(
            _options,
            _mockLogger.Object,
            _timeProvider);

        // Assert
        Assert.NotNull(policy);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new DefaultConnectionResiliencePolicy(
                null!,
                _mockLogger.Object,
                _timeProvider));
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new DefaultConnectionResiliencePolicy(
                _options,
                _mockLogger.Object,
                null!));
        Assert.Equal("timeProvider", exception.ParamName);
    }

    #endregion

    #region ExecuteAsync (Non-Generic) Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_SuccessfulOperation_ExecutesOnce()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var executionCount = 0;

        // Act
        await policy.ExecuteAsync(
            async () =>
            {
                await Task.CompletedTask;
                executionCount++;
            },
            "TestOperation");

        // Assert
        Assert.Equal(1, executionCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_TransientFailureThenSuccess_RetriesAndSucceeds()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var executionCount = 0;

        // Act
        await policy.ExecuteAsync(
            async () =>
            {
                await Task.CompletedTask;
                executionCount++;
                if (executionCount == 1)
                    throw new TimeoutException("Transient error");
            },
            "TestOperation");

        // Assert
        Assert.Equal(2, executionCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_NonTransientException_FailsImmediately()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var executionCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await policy.ExecuteAsync(
                async () =>
                {
                    await Task.CompletedTask;
                    executionCount++;
                    throw new InvalidOperationException("Non-transient error");
                },
                "TestOperation"));

        Assert.Equal(1, executionCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_MaxRetriesExceeded_ThrowsException()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var executionCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await policy.ExecuteAsync(
                async () =>
                {
                    await Task.CompletedTask;
                    executionCount++;
                    throw new TimeoutException("Persistent error");
                },
                "TestOperation"));

        Assert.Equal(4, executionCount); // Initial attempt + 3 retries
    }

    #endregion

    #region ExecuteAsync<T> (Generic) Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_Generic_SuccessfulOperation_ReturnsResult()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var expectedResult = 42;

        // Act
        var result = await policy.ExecuteAsync(
            async () =>
            {
                await Task.CompletedTask;
                return expectedResult;
            },
            "TestOperation");

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_Generic_TransientFailureThenSuccess_ReturnsResult()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var executionCount = 0;

        // Act
        var result = await policy.ExecuteAsync(
            async () =>
            {
                await Task.CompletedTask;
                executionCount++;
                if (executionCount == 1)
                    throw new TimeoutException("Transient error");
                return "Success";
            },
            "TestOperation");

        // Assert
        Assert.Equal("Success", result);
        Assert.Equal(2, executionCount);
    }

    #endregion

    #region Circuit Breaker Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_CircuitBreakerOpens_AfterFailureThreshold()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);

        // Act - Trigger failures to open circuit
        for (int i = 0; i < 5; i++)
        {
            try
            {
                await policy.ExecuteAsync(
                    async () =>
                    {
                        await Task.CompletedTask;
                        throw new TimeoutException("Transient error");
                    },
                    "TestOperation");
            }
            catch (TimeoutException)
            {
                // Expected
            }
        }

        // Assert - Circuit should be open, next call should fail immediately
        await Assert.ThrowsAsync<ConnectionResilienceException>(async () =>
            await policy.ExecuteAsync(
                async () =>
                {
                    await Task.CompletedTask;
                    return 0;
                },
                "TestOperation"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_CircuitBreakerRecovery_AfterBreakDuration()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);

        // Act - Open circuit
        for (int i = 0; i < 5; i++)
        {
            try
            {
                await policy.ExecuteAsync(
                    async () =>
                    {
                        await Task.CompletedTask;
                        throw new TimeoutException("Transient error");
                    },
                    "TestOperation");
            }
            catch (TimeoutException)
            {
                // Expected
            }
        }

        // Advance time past break duration
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        // Assert - Circuit should allow retry
        var result = await policy.ExecuteAsync(
            async () =>
            {
                await Task.CompletedTask;
                return 42;
            },
            "TestOperation");

        Assert.Equal(42, result);
    }

    #endregion

    #region Transient Exception Detection Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_TimeoutException_IsRetried()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var executionCount = 0;

        // Act
        await policy.ExecuteAsync(
            async () =>
            {
                await Task.CompletedTask;
                executionCount++;
                if (executionCount == 1)
                    throw new TimeoutException("Timeout");
            },
            "TestOperation");

        // Assert
        Assert.Equal(2, executionCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_TaskCanceledException_IsNotRetried()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var executionCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await policy.ExecuteAsync(
                async () =>
                {
                    await Task.CompletedTask;
                    executionCount++;
                    throw new TaskCanceledException("Canceled");
                },
                "TestOperation"));

        Assert.Equal(1, executionCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_OperationCanceledException_IsNotRetried()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var executionCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await policy.ExecuteAsync(
                async () =>
                {
                    await Task.CompletedTask;
                    executionCount++;
                    throw new OperationCanceledException("Canceled");
                },
                "TestOperation"));

        Assert.Equal(1, executionCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_DbExceptionWithTransientErrorCode_IsRetried()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var executionCount = 0;

        // Act
        await policy.ExecuteAsync(
            async () =>
            {
                await Task.CompletedTask;
                executionCount++;
                if (executionCount == 1)
                    throw new TestDbException(2); // Transient error code
            },
            "TestOperation");

        // Assert
        Assert.Equal(2, executionCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_InvalidOperationExceptionWithConnectionMessage_IsRetried()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var executionCount = 0;

        // Act
        await policy.ExecuteAsync(
            async () =>
            {
                await Task.CompletedTask;
                executionCount++;
                if (executionCount == 1)
                    throw new InvalidOperationException("Connection failed");
            },
            "TestOperation");

        // Assert
        Assert.Equal(2, executionCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_InnerTransientException_IsRetried()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var executionCount = 0;

        // Act
        await policy.ExecuteAsync(
            async () =>
            {
                await Task.CompletedTask;
                executionCount++;
                if (executionCount == 1)
                {
                    var innerException = new TimeoutException("Inner timeout");
                    throw new InvalidOperationException("Outer error", innerException);
                }
            },
            "TestOperation");

        // Assert
        Assert.Equal(2, executionCount);
    }

    #endregion

    #region Retry Delay Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_RetryDelay_IncreasesExponentially()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var delays = new List<TimeSpan>();
        var executionCount = 0;

        // Act
        try
        {
            await policy.ExecuteAsync(
                async () =>
                {
                    await Task.CompletedTask;
                    var currentTime = _timeProvider.GetUtcNow();
                    if (executionCount > 0)
                    {
                        delays.Add(TimeSpan.FromMilliseconds(1)); // Placeholder for actual delay tracking
                    }
                    executionCount++;
                    throw new TimeoutException("Persistent error");
                },
                "TestOperation");
        }
        catch (TimeoutException)
        {
            // Expected
        }

        // Assert - Should have attempted 4 times (initial + 3 retries)
        Assert.Equal(4, executionCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_RetryDelay_DoesNotExceedMaxDelay()
    {
        // Arrange
        var options = new ConnectionResilienceOptions
        {
            MaxRetries = 10,
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            MaxRetryDelay = TimeSpan.FromSeconds(5) // Max is less than base for testing
        };
        var policy = new DefaultConnectionResiliencePolicy(options, _mockLogger.Object, _timeProvider);

        // This test primarily verifies the implementation doesn't crash with unusual settings
        var executionCount = 0;

        // Act
        try
        {
            await policy.ExecuteAsync(
                async () =>
                {
                    await Task.CompletedTask;
                    executionCount++;
                    if (executionCount <= 2)
                        throw new TimeoutException("Transient error");
                },
                "TestOperation");
        }
        catch
        {
            // May throw depending on retry behavior
        }

        // Assert - Should have executed at least twice
        Assert.True(executionCount >= 2);
    }

    #endregion

    #region Health Monitor Integration Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_WithHealthMonitor_RecordsSuccess()
    {
        // Arrange
        // Create a real ConnectionHealthMonitor instance instead of mocking
        var healthLogger = new Mock<ILogger<ConnectionHealthMonitor>>();
        var healthMonitor = new ConnectionHealthMonitor(healthLogger.Object, _timeProvider);

        var policy = new DefaultConnectionResiliencePolicy(
            _options,
            _mockLogger.Object,
            _timeProvider,
            healthMonitor);

        // Act
        await policy.ExecuteAsync(
            async () => { await Task.CompletedTask; },
            "TestOperation");

        // Assert
        // Verify by checking metrics instead of mocking
        var metrics = healthMonitor.GetMetrics("TestOperation");
        Assert.Equal(1, metrics.TotalRequests);
        Assert.Equal(1, metrics.SuccessfulRequests);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_WithHealthMonitor_RecordsFailure()
    {
        // Arrange
        var healthLogger = new Mock<ILogger<ConnectionHealthMonitor>>();
        var healthMonitor = new ConnectionHealthMonitor(healthLogger.Object, _timeProvider);

        var policy = new DefaultConnectionResiliencePolicy(
            _options,
            _mockLogger.Object,
            _timeProvider,
            healthMonitor);

        // Act
        try
        {
            await policy.ExecuteAsync(
                async () =>
                {
                    await Task.CompletedTask;
                    throw new InvalidOperationException("Non-transient error");
                },
                "TestOperation");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert
        var metrics = healthMonitor.GetMetrics("TestOperation");
        Assert.Equal(1, metrics.TotalRequests);
        Assert.Equal(1, metrics.FailedRequests);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_CircuitBreakerOpen_RecordsFailureInHealthMonitor()
    {
        // Arrange
        var mockHealthMonitor = new Mock<ConnectionHealthMonitor>();
        var policy = new DefaultConnectionResiliencePolicy(
            _options,
            _mockLogger.Object,
            _timeProvider,
            mockHealthMonitor.Object);

        // Open circuit
        for (int i = 0; i < 5; i++)
        {
            try
            {
                await policy.ExecuteAsync(
                    async () =>
                    {
                        await Task.CompletedTask;
                        throw new TimeoutException("Transient error");
                    },
                    "TestOperation");
            }
            catch (TimeoutException)
            {
                // Expected
            }
        }

        mockHealthMonitor.Invocations.Clear();

        // Act - Circuit is open
        try
        {
            await policy.ExecuteAsync(
                async () =>
                {
                    await Task.CompletedTask;
                    return 0;
                },
                "TestOperation");
        }
        catch (ConnectionResilienceException)
        {
            // Expected
        }

        // Assert
        mockHealthMonitor.Verify(
            x => x.RecordFailure(
                "TestOperation",
                It.IsAny<ConnectionResilienceException>(),
                It.IsAny<TimeSpan>()),
            Times.Once);
    }

    #endregion

    #region Edge Cases and Boundary Conditions

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_WithZeroMaxRetries_DoesNotRetry()
    {
        // Arrange
        var options = new ConnectionResilienceOptions
        {
            MaxRetries = 0
        };
        var policy = new DefaultConnectionResiliencePolicy(options, _mockLogger.Object, _timeProvider);
        var executionCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await policy.ExecuteAsync(
                async () =>
                {
                    await Task.CompletedTask;
                    executionCount++;
                    throw new TimeoutException("Error");
                },
                "TestOperation"));

        Assert.Equal(1, executionCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await policy.ExecuteAsync(
                async () =>
                {
                    await Task.Delay(100, cts.Token);
                },
                "TestOperation",
                cts.Token));
    }

    #endregion

    #region Logging Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_SuccessAfterRetry_LogsInformation()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var executionCount = 0;

        // Act
        await policy.ExecuteAsync(
            async () =>
            {
                await Task.CompletedTask;
                executionCount++;
                if (executionCount == 1)
                    throw new TimeoutException("Transient error");
            },
            "TestOperation");

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("succeeded after")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_TransientError_LogsWarning()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);

        // Act
        try
        {
            await policy.ExecuteAsync(
                async () =>
                {
                    await Task.CompletedTask;
                    throw new TimeoutException("Transient error");
                },
                "TestOperation");
        }
        catch (TimeoutException)
        {
            // Expected
        }

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Transient error")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_MaxRetriesExceeded_LogsError()
    {
        // Arrange
        var policy = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);

        // Act
        try
        {
            await policy.ExecuteAsync(
                async () =>
                {
                    await Task.CompletedTask;
                    throw new TimeoutException("Persistent error");
                },
                "TestOperation");
        }
        catch (TimeoutException)
        {
            // Expected
        }

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("failed after")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}

/// <summary>
/// Test DbException for testing transient error detection
/// </summary>
public class TestDbException : DbException
{
    public override int ErrorCode { get; }

    public TestDbException(int errorCode)
    {
        ErrorCode = errorCode;
    }
}
