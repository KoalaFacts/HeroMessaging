using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration
{
    [Trait("Category", "Unit")]
    public sealed class SagaTimeoutHandlerTests
    {
        private readonly Mock<ILogger<SagaTimeoutHandler<TestSaga>>> _loggerMock;
        private readonly FakeTimeProvider _fakeTimeProvider;

        public SagaTimeoutHandlerTests()
        {
            _loggerMock = new Mock<ILogger<SagaTimeoutHandler<TestSaga>>>();
            _fakeTimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullServiceProvider_ThrowsArgumentNullException()
        {
            // Arrange
            var options = new SagaTimeoutOptions();

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new SagaTimeoutHandler<TestSaga>(null!, options, _loggerMock.Object));
            Assert.Equal("serviceProvider", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithNullOptions_ThrowsArgumentNullException()
        {
            // Arrange
            var services = new ServiceCollection().BuildServiceProvider();

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new SagaTimeoutHandler<TestSaga>(services, null!, _loggerMock.Object));
            Assert.Equal("options", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Arrange
            var services = new ServiceCollection().BuildServiceProvider();
            var options = new SagaTimeoutOptions();

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new SagaTimeoutHandler<TestSaga>(services, options, null!));
            Assert.Equal("logger", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithValidArguments_CreatesInstance()
        {
            // Arrange
            var services = new ServiceCollection().BuildServiceProvider();
            var options = new SagaTimeoutOptions();

            // Act
            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object);

            // Assert
            Assert.NotNull(handler);
        }

        #endregion

        #region SagaTimeoutOptions Tests

        [Fact]
        public void SagaTimeoutOptions_DefaultCheckInterval_IsOneMinute()
        {
            // Arrange & Act
            var options = new SagaTimeoutOptions();

            // Assert
            Assert.Equal(TimeSpan.FromMinutes(1), options.CheckInterval);
        }

        [Fact]
        public void SagaTimeoutOptions_DefaultTimeout_Is24Hours()
        {
            // Arrange & Act
            var options = new SagaTimeoutOptions();

            // Assert
            Assert.Equal(TimeSpan.FromHours(24), options.DefaultTimeout);
        }

        [Fact]
        public void SagaTimeoutOptions_DefaultEnabled_IsTrue()
        {
            // Arrange & Act
            var options = new SagaTimeoutOptions();

            // Assert
            Assert.True(options.Enabled);
        }

        [Fact]
        public void SagaTimeoutOptions_CanSetCheckInterval()
        {
            // Arrange
            var options = new SagaTimeoutOptions
            {
                // Act
                CheckInterval = TimeSpan.FromMinutes(5)
            };

            // Assert
            Assert.Equal(TimeSpan.FromMinutes(5), options.CheckInterval);
        }

        [Fact]
        public void SagaTimeoutOptions_CanSetDefaultTimeout()
        {
            // Arrange
            var options = new SagaTimeoutOptions
            {
                // Act
                DefaultTimeout = TimeSpan.FromHours(48)
            };

            // Assert
            Assert.Equal(TimeSpan.FromHours(48), options.DefaultTimeout);
        }

        [Fact]
        public void SagaTimeoutOptions_CanSetEnabled()
        {
            // Arrange
            var options = new SagaTimeoutOptions
            {
                // Act
                Enabled = false
            };

            // Assert
            Assert.False(options.Enabled);
        }

        #endregion

        #region ExecuteAsync - Timeout Detection Tests

        [Fact]
        public async Task ExecuteAsync_WithStaleSagas_MarksThemAsTimedOut()
        {
            // Arrange
            var repositoryMock = new Mock<ISagaRepository<TestSaga>>();
            var staleSaga = new TestSaga
            {
                CorrelationId = Guid.NewGuid(),
                CurrentState = "Processing",
                UpdatedAt = _fakeTimeProvider.GetUtcNow().AddHours(-25)
            };

            var processingComplete = new TaskCompletionSource<bool>();
            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([staleSaga]);
            repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TestSaga>(), It.IsAny<CancellationToken>()))
                .Callback(() => processingComplete.TrySetResult(true))
                .Returns(Task.CompletedTask);

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object, _fakeTimeProvider);
            var cts = new CancellationTokenSource();

            // Act
            await handler.StartAsync(cts.Token);

            // Wait for processing to complete with timeout
            var completedTask = await Task.WhenAny(processingComplete.Task, Task.Delay(1000));
            Assert.Same(processingComplete.Task, completedTask);

            await cts.CancelAsync();
            await handler.StopAsync(CancellationToken.None);

            // Assert
            repositoryMock.Verify(r => r.UpdateAsync(
                It.Is<TestSaga>(s => s.CurrentState == "TimedOut" && s.IsCompleted),
                It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task ExecuteAsync_WithNoStaleSagas_DoesNotUpdate()
        {
            // Arrange
            var repositoryMock = new Mock<ISagaRepository<TestSaga>>();
            var findCalled = new TaskCompletionSource<bool>();
            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Callback(() => findCalled.TrySetResult(true))
                .ReturnsAsync([]);

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object, _fakeTimeProvider);
            var cts = new CancellationTokenSource();

            // Act
            await handler.StartAsync(cts.Token);

            // Wait for at least one check to complete
            var completedTask = await Task.WhenAny(findCalled.Task, Task.Delay(1000));
            Assert.Same(findCalled.Task, completedTask);

            await cts.CancelAsync();
            await handler.StopAsync(CancellationToken.None);

            // Assert
            repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<TestSaga>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithMultipleStaleSagas_MarksAllAsTimedOut()
        {
            // Arrange
            var repositoryMock = new Mock<ISagaRepository<TestSaga>>();
            var staleSaga1 = new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "Processing" };
            var staleSaga2 = new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "Pending" };
            var staleSaga3 = new TestSaga { CorrelationId = Guid.NewGuid(), CurrentState = "Active" };

            var updateCount = 0;
            var allUpdated = new TaskCompletionSource<bool>();
            var callCount = 0;
            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    // Only return stale sagas on first call
                    callCount++;
                    return callCount == 1 ? new[] { staleSaga1, staleSaga2, staleSaga3 } : [];
                });
            repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TestSaga>(), It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    if (Interlocked.Increment(ref updateCount) == 3)
                        allUpdated.TrySetResult(true);
                })
                .Returns(Task.CompletedTask);

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object, _fakeTimeProvider);
            var cts = new CancellationTokenSource();

            // Act
            await handler.StartAsync(cts.Token);

            // Wait for all updates to complete
            var completedTask = await Task.WhenAny(allUpdated.Task, Task.Delay(1000));
            Assert.Same(allUpdated.Task, completedTask);

            await cts.CancelAsync();
            await handler.StopAsync(CancellationToken.None);

            // Assert
            repositoryMock.Verify(r => r.UpdateAsync(
                It.Is<TestSaga>(s => s.CurrentState == "TimedOut"),
                It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        #endregion

        #region ExecuteAsync - Concurrency Handling Tests

        [Fact]
        public async Task ExecuteAsync_WithConcurrentUpdate_HandlesGracefully()
        {
            // Arrange
            var repositoryMock = new Mock<ISagaRepository<TestSaga>>();
            var staleSaga = new TestSaga
            {
                CorrelationId = Guid.NewGuid(),
                CurrentState = "Processing"
            };

            var updateAttempted = new TaskCompletionSource<bool>();
            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([staleSaga]);

            repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TestSaga>(), It.IsAny<CancellationToken>()))
                .Callback(() => updateAttempted.TrySetResult(true))
                .ThrowsAsync(new SagaConcurrencyException(staleSaga.CorrelationId, 1, 0));

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object, _fakeTimeProvider);
            var cts = new CancellationTokenSource();

            // Act - Should not throw
            await handler.StartAsync(cts.Token);

            // Wait for update attempt
            var completedTask = await Task.WhenAny(updateAttempted.Task, Task.Delay(1000));
            Assert.Same(updateAttempted.Task, completedTask);

            await cts.CancelAsync();
            await handler.StopAsync(CancellationToken.None);

            // Assert - Should have attempted update
            repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<TestSaga>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task ExecuteAsync_WithUpdateException_ContinuesProcessing()
        {
            // Arrange
            var repositoryMock = new Mock<ISagaRepository<TestSaga>>();
            var staleSaga = new TestSaga
            {
                CorrelationId = Guid.NewGuid(),
                CurrentState = "Processing"
            };

            var callCount = 0;
            var secondCallComplete = new TaskCompletionSource<bool>();
            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var count = Interlocked.Increment(ref callCount);
                    if (count >= 2)
                        secondCallComplete.TrySetResult(true);
                    return [staleSaga];
                });

            repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TestSaga>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Update failed"));

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object, _fakeTimeProvider);
            var cts = new CancellationTokenSource();

            // Act
            await handler.StartAsync(cts.Token);

            // Advance time to trigger multiple checks
            _fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(100));

            // Wait for at least 2 calls
            var completedTask = await Task.WhenAny(secondCallComplete.Task, Task.Delay(1000));
            Assert.Same(secondCallComplete.Task, completedTask);

            await cts.CancelAsync();
            await handler.StopAsync(CancellationToken.None);

            // Assert - Should have attempted multiple checks despite errors
            Assert.True(callCount >= 2, $"Expected at least 2 checks, got {callCount}");
        }

        #endregion

        #region ExecuteAsync - Cancellation Tests

        [Fact]
        public async Task ExecuteAsync_WithCancellation_StopsProcessing()
        {
            // Arrange
            var repositoryMock = new Mock<ISagaRepository<TestSaga>>();
            var findCalled = new TaskCompletionSource<bool>();
            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Callback(() => findCalled.TrySetResult(true))
                .ReturnsAsync([]);

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromSeconds(10), // Long interval
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object, _fakeTimeProvider);
            var cts = new CancellationTokenSource();

            // Act
            await handler.StartAsync(cts.Token);

            // Wait for first check to start
            await Task.WhenAny(findCalled.Task, Task.Delay(500));

            await cts.CancelAsync();

            // BackgroundService should stop gracefully when StopAsync is called
            await handler.StopAsync(CancellationToken.None);

            // Assert - if we get here without hanging, the test passes
            Assert.True(true);
        }

        [Fact]
        public async Task StopAsync_StopsBackgroundService()
        {
            // Arrange
            var repositoryMock = new Mock<ISagaRepository<TestSaga>>();
            var findCalled = new TaskCompletionSource<bool>();
            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Callback(() => findCalled.TrySetResult(true))
                .ReturnsAsync([]);

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object, _fakeTimeProvider);

            // Act
            await handler.StartAsync(CancellationToken.None);

            // Wait for first check
            await Task.WhenAny(findCalled.Task, Task.Delay(500));

            await handler.StopAsync(CancellationToken.None);

            // Assert - Should complete without error
            Assert.True(true);
        }

        #endregion

        #region State Transition Tests

        [Fact]
        public async Task ExecuteAsync_MarksTimedOutSagaAsCompleted()
        {
            // Arrange
            var repositoryMock = new Mock<ISagaRepository<TestSaga>>();
            var staleSaga = new TestSaga
            {
                CorrelationId = Guid.NewGuid(),
                CurrentState = "Processing",
                IsCompleted = false
            };

            var updateCalled = new TaskCompletionSource<bool>();
            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([staleSaga]);
            repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TestSaga>(), It.IsAny<CancellationToken>()))
                .Callback(() => updateCalled.TrySetResult(true))
                .Returns(Task.CompletedTask);

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object, _fakeTimeProvider);
            var cts = new CancellationTokenSource();

            // Act
            await handler.StartAsync(cts.Token);

            // Wait for update to complete
            var completedTask = await Task.WhenAny(updateCalled.Task, Task.Delay(1000));
            Assert.Same(updateCalled.Task, completedTask);

            await cts.CancelAsync();
            await handler.StopAsync(CancellationToken.None);

            // Assert
            Assert.True(staleSaga.IsCompleted);
        }

        [Fact]
        public async Task ExecuteAsync_TransitionsToTimedOutState()
        {
            // Arrange
            var repositoryMock = new Mock<ISagaRepository<TestSaga>>();
            var staleSaga = new TestSaga
            {
                CorrelationId = Guid.NewGuid(),
                CurrentState = "Processing"
            };

            var updateCalled = new TaskCompletionSource<bool>();
            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([staleSaga]);
            repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TestSaga>(), It.IsAny<CancellationToken>()))
                .Callback(() => updateCalled.TrySetResult(true))
                .Returns(Task.CompletedTask);

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object, _fakeTimeProvider);
            var cts = new CancellationTokenSource();

            // Act
            await handler.StartAsync(cts.Token);

            // Wait for update to complete
            var completedTask = await Task.WhenAny(updateCalled.Task, Task.Delay(1000));
            Assert.Same(updateCalled.Task, completedTask);

            await cts.CancelAsync();
            await handler.StopAsync(CancellationToken.None);

            // Assert
            Assert.Equal("TimedOut", staleSaga.CurrentState);
        }

        #endregion

        #region Helper Methods

        private IServiceProvider CreateServiceProviderWithRepository(ISagaRepository<TestSaga> repository)
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => repository);
            return services.BuildServiceProvider();
        }

        #endregion

        #region Test Helper Classes

        public class TestSaga : ISaga
        {
            public Guid CorrelationId { get; set; }
            public string CurrentState { get; set; } = "Initial";
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }
            public bool IsCompleted { get; set; }
            public int Version { get; set; }
        }

        #endregion
    }
}
