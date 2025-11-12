using HeroMessaging.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Resilience
{
    [Trait("Category", "Unit")]
    public sealed class ConnectionHealthMonitorTests : IAsyncDisposable
    {
        private readonly Mock<ILogger<ConnectionHealthMonitor>> _loggerMock;
        private readonly FakeTimeProvider _timeProvider;
        private ConnectionHealthMonitor? _monitor;

        public ConnectionHealthMonitorTests()
        {
            _loggerMock = new Mock<ILogger<ConnectionHealthMonitor>>();
            _timeProvider = new FakeTimeProvider();
        }

        public async ValueTask DisposeAsync()
        {
            if (_monitor != null)
            {
                await _monitor.StopAsync(CancellationToken.None);
                _monitor.Dispose();
            }
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullLogger_CreatesInstance()
        {
            // Act
            _monitor = new ConnectionHealthMonitor(null!, _timeProvider);

            // Assert
            Assert.NotNull(_monitor);
        }

        [Fact]
        public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new ConnectionHealthMonitor(_loggerMock.Object, null!));

            Assert.Equal("timeProvider", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithValidArguments_CreatesInstance()
        {
            // Act
            _monitor = new ConnectionHealthMonitor(_loggerMock.Object, _timeProvider);

            // Assert
            Assert.NotNull(_monitor);
        }

        [Fact]
        public void Constructor_WithNullOptions_UsesDefaultOptions()
        {
            // Act
            _monitor = new ConnectionHealthMonitor(_loggerMock.Object, _timeProvider, null);

            // Assert
            Assert.NotNull(_monitor);
        }

        #endregion

        #region GetMetrics Tests

        [Fact]
        public void GetMetrics_WithNewOperation_CreatesNewMetrics()
        {
            // Arrange
            _monitor = new ConnectionHealthMonitor(_loggerMock.Object, _timeProvider);

            // Act
            var metrics = _monitor.GetMetrics("TestOperation");

            // Assert
            Assert.NotNull(metrics);
            Assert.Equal(0, metrics.TotalRequests);
        }

        [Fact]
        public void GetMetrics_WithSameOperation_ReturnsSameInstance()
        {
            // Arrange
            _monitor = new ConnectionHealthMonitor(_loggerMock.Object, _timeProvider);

            // Act
            var metrics1 = _monitor.GetMetrics("TestOperation");
            var metrics2 = _monitor.GetMetrics("TestOperation");

            // Assert
            Assert.Same(metrics1, metrics2);
        }

        #endregion

        #region RecordSuccess Tests

        [Fact]
        public void RecordSuccess_UpdatesMetrics()
        {
            // Arrange
            _monitor = new ConnectionHealthMonitor(_loggerMock.Object, _timeProvider);

            // Act
            _monitor.RecordSuccess("TestOperation", TimeSpan.FromMilliseconds(100));

            // Assert
            var metrics = _monitor.GetMetrics("TestOperation");
            Assert.Equal(1, metrics.SuccessfulRequests);
        }

        #endregion

        #region RecordFailure Tests

        [Fact]
        public void RecordFailure_UpdatesMetrics()
        {
            // Arrange
            _monitor = new ConnectionHealthMonitor(_loggerMock.Object, _timeProvider);
            var exception = new InvalidOperationException("Test error");

            // Act
            _monitor.RecordFailure("TestOperation", exception, TimeSpan.FromMilliseconds(100));

            // Assert
            var metrics = _monitor.GetMetrics("TestOperation");
            Assert.Equal(1, metrics.FailedRequests);
        }

        #endregion

        #region GetOverallHealth Tests

        [Fact]
        public void GetOverallHealth_WithNoMetrics_ReturnsUnknown()
        {
            // Arrange
            _monitor = new ConnectionHealthMonitor(_loggerMock.Object, _timeProvider);

            // Act
            var result = _monitor.GetOverallHealth();

            // Assert
            Assert.Equal(ConnectionHealthStatus.Unknown, result);
        }

        [Fact]
        public void GetOverallHealth_WithAllHealthyOperations_ReturnsHealthy()
        {
            // Arrange
            _monitor = new ConnectionHealthMonitor(_loggerMock.Object, _timeProvider);
            _monitor.RecordSuccess("Op1", TimeSpan.FromMilliseconds(100));
            _monitor.RecordSuccess("Op2", TimeSpan.FromMilliseconds(100));

            // Act
            var result = _monitor.GetOverallHealth();

            // Assert
            Assert.Equal(ConnectionHealthStatus.Healthy, result);
        }

        [Fact]
        public void GetOverallHealth_WithSomeUnhealthyOperations_ReturnsDegraded()
        {
            // Arrange
            var options = new ConnectionHealthOptions { UnhealthyFailureRate = 0.5 };
            _monitor = new ConnectionHealthMonitor(_loggerMock.Object, _timeProvider, options);

            // Make Op1 unhealthy (100% failure rate)
            _monitor.RecordFailure("Op1", new InvalidOperationException(), TimeSpan.FromMilliseconds(100));

            // Make Op2 healthy
            _monitor.RecordSuccess("Op2", TimeSpan.FromMilliseconds(100));

            // Act
            var result = _monitor.GetOverallHealth();

            // Assert
            Assert.Equal(ConnectionHealthStatus.Degraded, result); // 50% unhealthy < threshold
        }

        [Fact]
        public void GetOverallHealth_WithMostUnhealthyOperations_ReturnsUnhealthy()
        {
            // Arrange
            var options = new ConnectionHealthOptions { UnhealthyFailureRate = 0.5 };
            _monitor = new ConnectionHealthMonitor(_loggerMock.Object, _timeProvider, options);

            // Make Op1 unhealthy
            _monitor.RecordFailure("Op1", new InvalidOperationException(), TimeSpan.FromMilliseconds(100));

            // Make Op2 unhealthy
            _monitor.RecordFailure("Op2", new InvalidOperationException(), TimeSpan.FromMilliseconds(100));

            // Make Op3 healthy
            _monitor.RecordSuccess("Op3", TimeSpan.FromMilliseconds(100));

            // Act
            var result = _monitor.GetOverallHealth();

            // Assert
            Assert.Equal(ConnectionHealthStatus.Unhealthy, result); // 67% unhealthy > 50%
        }

        #endregion

        #region GetHealthReport Tests

        [Fact]
        public void GetHealthReport_ReturnsReportWithTimestamp()
        {
            // Arrange
            _monitor = new ConnectionHealthMonitor(_loggerMock.Object, _timeProvider);
            var expectedTime = _timeProvider.GetUtcNow();

            // Act
            var report = _monitor.GetHealthReport();

            // Assert
            Assert.NotNull(report);
            Assert.Equal(expectedTime, report.Timestamp);
        }

        [Fact]
        public void GetHealthReport_IncludesAllOperationMetrics()
        {
            // Arrange
            _monitor = new ConnectionHealthMonitor(_loggerMock.Object, _timeProvider);
            _monitor.RecordSuccess("Op1", TimeSpan.FromMilliseconds(100));
            _monitor.RecordFailure("Op2", new InvalidOperationException("Error"), TimeSpan.FromMilliseconds(200));

            // Act
            var report = _monitor.GetHealthReport();

            // Assert
            Assert.Equal(2, report.OperationMetrics.Count);
            Assert.Contains("Op1", report.OperationMetrics.Keys);
            Assert.Contains("Op2", report.OperationMetrics.Keys);
        }

        [Fact]
        public void GetHealthReport_IncludesOperationDetails()
        {
            // Arrange
            _monitor = new ConnectionHealthMonitor(_loggerMock.Object, _timeProvider);
            _monitor.RecordSuccess("TestOp", TimeSpan.FromMilliseconds(100));
            _monitor.RecordFailure("TestOp", new InvalidOperationException("Test error"), TimeSpan.FromMilliseconds(200));

            // Act
            var report = _monitor.GetHealthReport();

            // Assert
            var opData = report.OperationMetrics["TestOp"];
            Assert.Equal(2, opData.TotalRequests);
            Assert.Equal(1, opData.SuccessfulRequests);
            Assert.Equal(1, opData.FailedRequests);
            Assert.Equal(0.5, opData.FailureRate);
            Assert.Equal("Test error", opData.LastFailureReason);
        }

        #endregion
    }
}
