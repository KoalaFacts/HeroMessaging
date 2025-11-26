using HeroMessaging.Abstractions.Sagas;
using HeroMessaging.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Orchestration
{
    [Trait("Category", "Unit")]
    public sealed class SagaTimeoutHandlerTests
    {
        private readonly Mock<ILogger<SagaTimeoutHandler<TestSaga>>> _loggerMock;

        public SagaTimeoutHandlerTests()
        {
            _loggerMock = new Mock<ILogger<SagaTimeoutHandler<TestSaga>>>();
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
            var options = new SagaTimeoutOptions();

            // Act
            options.CheckInterval = TimeSpan.FromMinutes(5);

            // Assert
            Assert.Equal(TimeSpan.FromMinutes(5), options.CheckInterval);
        }

        [Fact]
        public void SagaTimeoutOptions_CanSetDefaultTimeout()
        {
            // Arrange
            var options = new SagaTimeoutOptions();

            // Act
            options.DefaultTimeout = TimeSpan.FromHours(48);

            // Assert
            Assert.Equal(TimeSpan.FromHours(48), options.DefaultTimeout);
        }

        [Fact]
        public void SagaTimeoutOptions_CanSetEnabled()
        {
            // Arrange
            var options = new SagaTimeoutOptions();

            // Act
            options.Enabled = false;

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
                UpdatedAt = DateTimeOffset.UtcNow.AddHours(-25)
            };

            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { staleSaga });

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object);
            var cts = new CancellationTokenSource();

            // Act
            var executeTask = handler.StartAsync(cts.Token);
            await Task.Delay(100); // Give it time to process
            cts.Cancel();

            try
            {
                await executeTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

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
            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TestSaga>());

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object);
            var cts = new CancellationTokenSource();

            // Act
            var executeTask = handler.StartAsync(cts.Token);
            await Task.Delay(100);
            cts.Cancel();

            try
            {
                await executeTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

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

            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { staleSaga1, staleSaga2, staleSaga3 });

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object);
            var cts = new CancellationTokenSource();

            // Act
            var executeTask = handler.StartAsync(cts.Token);
            await Task.Delay(100);
            cts.Cancel();

            try
            {
                await executeTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

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

            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { staleSaga });

            repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TestSaga>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SagaConcurrencyException(staleSaga.CorrelationId, 1, 0));

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object);
            var cts = new CancellationTokenSource();

            // Act - Should not throw
            var executeTask = handler.StartAsync(cts.Token);
            await Task.Delay(100);
            cts.Cancel();

            try
            {
                await executeTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

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
            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    callCount++;
                    return new[] { staleSaga };
                });

            repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<TestSaga>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Update failed"));

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object);
            var cts = new CancellationTokenSource();

            // Act
            var executeTask = handler.StartAsync(cts.Token);
            await Task.Delay(150); // Allow multiple checks
            cts.Cancel();

            try
            {
                await executeTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

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
            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TestSaga>());

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromSeconds(10), // Long interval
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object);
            var cts = new CancellationTokenSource();

            // Act
            var executeTask = handler.StartAsync(cts.Token);
            await Task.Delay(50); // Short delay
            cts.Cancel();

            // Should complete quickly when cancelled
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await executeTask);
        }

        [Fact]
        public async Task StopAsync_StopsBackgroundService()
        {
            // Arrange
            var repositoryMock = new Mock<ISagaRepository<TestSaga>>();
            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TestSaga>());

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object);

            // Act
            await handler.StartAsync(CancellationToken.None);
            await Task.Delay(100);
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

            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { staleSaga });

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object);
            var cts = new CancellationTokenSource();

            // Act
            var executeTask = handler.StartAsync(cts.Token);
            await Task.Delay(100);
            cts.Cancel();

            try
            {
                await executeTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

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

            repositoryMock.Setup(r => r.FindStaleAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { staleSaga });

            var services = CreateServiceProviderWithRepository(repositoryMock.Object);
            var options = new SagaTimeoutOptions
            {
                CheckInterval = TimeSpan.FromMilliseconds(50),
                DefaultTimeout = TimeSpan.FromHours(24)
            };

            var handler = new SagaTimeoutHandler<TestSaga>(services, options, _loggerMock.Object);
            var cts = new CancellationTokenSource();

            // Act
            var executeTask = handler.StartAsync(cts.Token);
            await Task.Delay(100);
            cts.Cancel();

            try
            {
                await executeTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            // Assert
            Assert.Equal("TimedOut", staleSaga.CurrentState);
        }

        #endregion

        #region Helper Methods

        private IServiceProvider CreateServiceProviderWithRepository(ISagaRepository<TestSaga> repository)
        {
            var services = new ServiceCollection();
            services.AddScoped<ISagaRepository<TestSaga>>(_ => repository);
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
