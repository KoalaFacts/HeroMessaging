using System.Data.Common;
using HeroMessaging.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit;

[Trait("Category", "Unit")]
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
            BaseRetryDelay = TimeSpan.FromMilliseconds(100),
            MaxRetryDelay = TimeSpan.FromSeconds(10)
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new DefaultConnectionResiliencePolicy(
            null!,
            _mockLogger.Object,
            _timeProvider));
        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new DefaultConnectionResiliencePolicy(
            _options,
            _mockLogger.Object,
            null!));
        Assert.Equal("timeProvider", ex.ParamName);
    }

    #endregion

    #region Success Tests

    [Fact]
    public async Task ExecuteAsync_WithSuccessOnFirstAttempt_ReturnsResult()
    {
        // Arrange
        var sut = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var expectedResult = 42;

        // Act
        var result = await sut.ExecuteAsync(async () =>
        {
            await Task.CompletedTask;
            return expectedResult;
        }, "TestOperation");

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task ExecuteAsync_VoidOverload_WithSuccessOnFirstAttempt_Completes()
    {
        // Arrange
        var sut = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var executed = false;

        // Act
        await sut.ExecuteAsync(async () =>
        {
            await Task.CompletedTask;
            executed = true;
        }, "TestOperation");

        // Assert
        Assert.True(executed);
    }

    #endregion

    #region Retry Tests

    [Fact]
    public async Task ExecuteAsync_WithTransientException_RetriesUpToMaxRetries()
    {
        // Arrange
        var sut = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await sut.ExecuteAsync(async () =>
            {
                attemptCount++;
                await Task.CompletedTask;
                throw new TimeoutException("Transient error");
            }, "TestOperation");
        });

        Assert.Equal(4, attemptCount); // Initial + 3 retries
    }

    [Fact]
    public async Task ExecuteAsync_WithTransientExceptionThenSuccess_ReturnsResult()
    {
        // Arrange
        var sut = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var attemptCount = 0;
        var expectedResult = 42;

        // Act
        var result = await sut.ExecuteAsync(async () =>
        {
            attemptCount++;
            await Task.CompletedTask;
            if (attemptCount < 3)
            {
                throw new TimeoutException("Transient error");
            }
            return expectedResult;
        }, "TestOperation");

        // Assert
        Assert.Equal(expectedResult, result);
        Assert.Equal(3, attemptCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonTransientException_DoesNotRetry()
    {
        // Arrange
        var sut = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await sut.ExecuteAsync(async () =>
            {
                attemptCount++;
                await Task.CompletedTask;
                throw new InvalidOperationException("Non-transient error");
            }, "TestOperation");
        });

        Assert.Equal(1, attemptCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithTaskCanceledException_DoesNotRetry()
    {
        // Arrange
        var sut = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await sut.ExecuteAsync(async () =>
            {
                attemptCount++;
                await Task.CompletedTask;
                throw new TaskCanceledException("Cancelled");
            }, "TestOperation");
        });

        Assert.Equal(1, attemptCount);
    }

    #endregion

    #region DbException Tests

    [Fact]
    public async Task ExecuteAsync_WithTransientDbException_Retries()
    {
        // Arrange
        var sut = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<TestDbException>(async () =>
        {
            await sut.ExecuteAsync(async () =>
            {
                attemptCount++;
                await Task.CompletedTask;
                throw new TestDbException(2); // Timeout error code
            }, "TestOperation");
        });

        Assert.Equal(4, attemptCount); // Initial + 3 retries
    }

    [Fact]
    public async Task ExecuteAsync_WithDbExceptionContainingTimeout_Retries()
    {
        // Arrange
        var sut = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<TestDbException>(async () =>
        {
            await sut.ExecuteAsync(async () =>
            {
                attemptCount++;
                await Task.CompletedTask;
                throw new TestDbException(999, "Timeout occurred");
            }, "TestOperation");
        });

        Assert.Equal(4, attemptCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithDbExceptionContainingConnection_Retries()
    {
        // Arrange
        var sut = new DefaultConnectionResiliencePolicy(_options, _mockLogger.Object, _timeProvider);
        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<TestDbException>(async () =>
        {
            await sut.ExecuteAsync(async () =>
            {
                attemptCount++;
                await Task.CompletedTask;
                throw new TestDbException(999, "Connection failure");
            }, "TestOperation");
        });

        Assert.Equal(4, attemptCount);
    }

    #endregion

    #region Circuit Breaker Tests

    [Fact]
    public async Task ExecuteAsync_WithCircuitOpen_ThrowsConnectionResilienceException()
    {
        // Arrange
        var options = new ConnectionResilienceOptions
        {
            MaxRetries = 1,
            BaseRetryDelay = TimeSpan.FromMilliseconds(10),
            CircuitBreakerOptions = new CircuitBreakerOptions
            {
                FailureThreshold = 2,
                BreakDuration = TimeSpan.FromSeconds(30)
            }
        };
        var sut = new DefaultConnectionResiliencePolicy(options, _mockLogger.Object, _timeProvider);

        // Open the circuit with failures
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await sut.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    throw new TimeoutException("Failure");
                }, "TestOperation");
            }
            catch { }
        }

        // Act & Assert - Circuit should be open
        var ex = await Assert.ThrowsAsync<ConnectionResilienceException>(async () =>
        {
            await sut.ExecuteAsync(async () =>
            {
                await Task.CompletedTask;
                return 42;
            }, "TestOperation");
        });

        Assert.Contains("Circuit breaker is open", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WithCircuitOpenThenClosed_AllowsRequestsAfterBreakDuration()
    {
        // Arrange
        var options = new ConnectionResilienceOptions
        {
            MaxRetries = 1,
            BaseRetryDelay = TimeSpan.FromMilliseconds(10),
            CircuitBreakerOptions = new CircuitBreakerOptions
            {
                FailureThreshold = 2,
                BreakDuration = TimeSpan.FromSeconds(30)
            }
        };
        var sut = new DefaultConnectionResiliencePolicy(options, _mockLogger.Object, _timeProvider);

        // Open the circuit
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await sut.ExecuteAsync(async () =>
                {
                    await Task.CompletedTask;
                    throw new TimeoutException("Failure");
                }, "TestOperation");
            }
            catch { }
        }

        // Advance time past break duration
        _timeProvider.Advance(TimeSpan.FromSeconds(31));

        // Act - Should succeed as circuit is half-open
        var result = await sut.ExecuteAsync(async () =>
        {
            await Task.CompletedTask;
            return 42;
        }, "TestOperation");

        // Assert
        Assert.Equal(42, result);
    }

    #endregion

    #region Delay Calculation Tests

    [Fact]
    public async Task ExecuteAsync_WithRetries_UsesExponentialBackoffWithJitter()
    {
        // Arrange
        var options = new ConnectionResilienceOptions
        {
            MaxRetries = 3,
            BaseRetryDelay = TimeSpan.FromMilliseconds(100),
            MaxRetryDelay = TimeSpan.FromSeconds(10)
        };
        var sut = new DefaultConnectionResiliencePolicy(options, _mockLogger.Object, _timeProvider);
        var attemptTimes = new List<DateTimeOffset>();

        // Act
        try
        {
            await sut.ExecuteAsync(async () =>
            {
                attemptTimes.Add(_timeProvider.GetUtcNow());
                await Task.CompletedTask;
                throw new TimeoutException("Transient error");
            }, "TestOperation");
        }
        catch { }

        // Assert - Check that delays are increasing
        Assert.Equal(4, attemptTimes.Count);

        // First retry should have some delay
        var firstDelay = attemptTimes[1] - attemptTimes[0];
        Assert.True(firstDelay >= TimeSpan.FromMilliseconds(80)); // Base * 1 with jitter

        // Second retry should have longer delay
        var secondDelay = attemptTimes[2] - attemptTimes[1];
        Assert.True(secondDelay > firstDelay);
    }

    [Fact]
    public async Task ExecuteAsync_WithLongRetries_RespectsMaxDelay()
    {
        // Arrange
        var options = new ConnectionResilienceOptions
        {
            MaxRetries = 10,
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            MaxRetryDelay = TimeSpan.FromSeconds(20)
        };
        var sut = new DefaultConnectionResiliencePolicy(options, _mockLogger.Object, _timeProvider);
        var attemptTimes = new List<DateTimeOffset>();

        // Act
        try
        {
            await sut.ExecuteAsync(async () =>
            {
                attemptTimes.Add(_timeProvider.GetUtcNow());
                await Task.CompletedTask;
                throw new TimeoutException("Transient error");
            }, "TestOperation");
        }
        catch { }

        // Assert - Last delay should not exceed max
        if (attemptTimes.Count > 2)
        {
            var lastDelay = attemptTimes[^1] - attemptTimes[^2];
            Assert.True(lastDelay <= TimeSpan.FromSeconds(21)); // Max + some tolerance for jitter
        }
    }

    #endregion

    #region ConnectionResilienceOptions Tests

    [Fact]
    public void ConnectionResilienceOptions_HasExpectedDefaults()
    {
        // Arrange & Act
        var options = new ConnectionResilienceOptions();

        // Assert
        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), options.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), options.MaxRetryDelay);
        Assert.NotNull(options.CircuitBreakerOptions);
    }

    #endregion

    #region ConnectionResilienceException Tests

    [Fact]
    public void ConnectionResilienceException_WithDefaultConstructor_HasDefaultMessage()
    {
        // Arrange & Act
        var exception = new ConnectionResilienceException();

        // Assert
        Assert.Equal("Connection resilience failed", exception.Message);
    }

    [Fact]
    public void ConnectionResilienceException_WithCustomMessage_HasCustomMessage()
    {
        // Arrange & Act
        var exception = new ConnectionResilienceException("Custom message");

        // Assert
        Assert.Equal("Custom message", exception.Message);
    }

    [Fact]
    public void ConnectionResilienceException_WithInnerException_HasInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner");

        // Act
        var exception = new ConnectionResilienceException("Custom message", innerException);

        // Assert
        Assert.Equal("Custom message", exception.Message);
        Assert.Equal(innerException, exception.InnerException);
    }

    #endregion

    // Test helper class to simulate DbException
    private class TestDbException : DbException
    {
        private readonly int _errorCode;
        private readonly string _message;

        public TestDbException(int errorCode, string message = "Test DB error") : base(message)
        {
            _errorCode = errorCode;
            _message = message;
        }

        public override int ErrorCode => _errorCode;
        public override string Message => _message;
    }
}
