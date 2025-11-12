using HeroMessaging.Resilience;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace HeroMessaging.Tests.Unit.Resilience
{
    [Trait("Category", "Unit")]
    public sealed class ConnectionHealthMetricsTests
    {
        private readonly FakeTimeProvider _timeProvider;

        public ConnectionHealthMetricsTests()
        {
            _timeProvider = new FakeTimeProvider();
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullTimeProvider_ThrowsArgumentNullException()
        {
            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new ConnectionHealthMetrics(null!));

            Assert.Equal("timeProvider", ex.ParamName);
        }

        [Fact]
        public void Constructor_WithValidTimeProvider_CreatesInstance()
        {
            // Act
            var metrics = new ConnectionHealthMetrics(_timeProvider);

            // Assert
            Assert.NotNull(metrics);
            Assert.Equal(0, metrics.TotalRequests);
            Assert.Equal(0, metrics.SuccessfulRequests);
            Assert.Equal(0, metrics.FailedRequests);
            Assert.Equal(0, metrics.FailureRate);
            Assert.False(metrics.IsCircuitBreakerOpen);
        }

        #endregion

        #region RecordSuccess Tests

        [Fact]
        public void RecordSuccess_IncrementsCounters()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);

            // Act
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));

            // Assert
            Assert.Equal(1, metrics.TotalRequests);
            Assert.Equal(1, metrics.SuccessfulRequests);
            Assert.Equal(0, metrics.FailedRequests);
            Assert.Equal(0, metrics.FailureRate);
        }

        [Fact]
        public void RecordSuccess_UpdatesAverageResponseTime()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);

            // Act
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(200));

            // Assert
            Assert.Equal(TimeSpan.FromMilliseconds(150), metrics.AverageResponseTime);
        }

        [Fact]
        public void RecordSuccess_ClosesCircuitBreaker()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);
            metrics.SetCircuitBreakerState(true);

            // Act
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));

            // Assert
            Assert.False(metrics.IsCircuitBreakerOpen);
        }

        #endregion

        #region RecordFailure Tests

        [Fact]
        public void RecordFailure_IncrementsCounters()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);
            var exception = new InvalidOperationException("Test error");

            // Act
            metrics.RecordFailure(exception, TimeSpan.FromMilliseconds(100));

            // Assert
            Assert.Equal(1, metrics.TotalRequests);
            Assert.Equal(0, metrics.SuccessfulRequests);
            Assert.Equal(1, metrics.FailedRequests);
            Assert.Equal(1.0, metrics.FailureRate);
        }

        [Fact]
        public void RecordFailure_UpdatesLastFailureTime()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);
            var exception = new InvalidOperationException("Test error");
            var expectedTime = _timeProvider.GetUtcNow();

            // Act
            metrics.RecordFailure(exception, TimeSpan.FromMilliseconds(100));

            // Assert
            Assert.Equal(expectedTime, metrics.LastFailureTime);
        }

        [Fact]
        public void RecordFailure_UpdatesLastFailureReason()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);
            var exception = new InvalidOperationException("Test error message");

            // Act
            metrics.RecordFailure(exception, TimeSpan.FromMilliseconds(100));

            // Assert
            Assert.Equal("Test error message", metrics.LastFailureReason);
        }

        [Fact]
        public void RecordFailure_CalculatesCorrectFailureRate()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);
            var exception = new InvalidOperationException("Test error");

            // Act
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));
            metrics.RecordFailure(exception, TimeSpan.FromMilliseconds(100));
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));

            // Assert
            Assert.Equal(4, metrics.TotalRequests);
            Assert.Equal(3, metrics.SuccessfulRequests);
            Assert.Equal(1, metrics.FailedRequests);
            Assert.Equal(0.25, metrics.FailureRate);
        }

        #endregion

        #region Circuit Breaker Tests

        [Fact]
        public void SetCircuitBreakerState_WithTrue_OpensCircuitBreaker()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);

            // Act
            metrics.SetCircuitBreakerState(true);

            // Assert
            Assert.True(metrics.IsCircuitBreakerOpen);
        }

        [Fact]
        public void SetCircuitBreakerState_WithFalse_ClosesCircuitBreaker()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);
            metrics.SetCircuitBreakerState(true);

            // Act
            metrics.SetCircuitBreakerState(false);

            // Assert
            Assert.False(metrics.IsCircuitBreakerOpen);
        }

        #endregion

        #region IsUnhealthy Tests

        [Fact]
        public void IsUnhealthy_WithLowFailureRate_ReturnsFalse()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));
            metrics.RecordFailure(new InvalidOperationException(), TimeSpan.FromMilliseconds(100));

            // Act
            var result = metrics.IsUnhealthy(0.5);

            // Assert
            Assert.False(result); // 33% failure rate < 50% threshold
        }

        [Fact]
        public void IsUnhealthy_WithHighFailureRate_ReturnsTrue()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));
            metrics.RecordFailure(new InvalidOperationException(), TimeSpan.FromMilliseconds(100));
            metrics.RecordFailure(new InvalidOperationException(), TimeSpan.FromMilliseconds(100));

            // Act
            var result = metrics.IsUnhealthy(0.5);

            // Assert
            Assert.True(result); // 67% failure rate > 50% threshold
        }

        [Fact]
        public void IsUnhealthy_WithOpenCircuitBreaker_ReturnsTrue()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));
            metrics.SetCircuitBreakerState(true);

            // Act
            var result = metrics.IsUnhealthy(0.5);

            // Assert
            Assert.True(result); // Circuit breaker open
        }

        #endregion

        #region CleanupOldData Tests

        [Fact]
        public void CleanupOldData_RemovesOldResults()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));
            _timeProvider.Advance(TimeSpan.FromHours(2));
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(200));

            // Act
            var cutoff = _timeProvider.GetUtcNow() - TimeSpan.FromHours(1);
            metrics.CleanupOldData(cutoff);

            // Assert
            // Average should only include recent result
            Assert.Equal(TimeSpan.FromMilliseconds(200), metrics.AverageResponseTime);
        }

        [Fact]
        public void CleanupOldData_KeepsRecentResults()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(200));

            // Act
            var cutoff = _timeProvider.GetUtcNow() - TimeSpan.FromHours(1);
            metrics.CleanupOldData(cutoff);

            // Assert
            Assert.Equal(TimeSpan.FromMilliseconds(150), metrics.AverageResponseTime);
        }

        #endregion

        #region AverageResponseTime Tests

        [Fact]
        public void AverageResponseTime_WithNoResults_ReturnsZero()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);

            // Act
            var result = metrics.AverageResponseTime;

            // Assert
            Assert.Equal(TimeSpan.Zero, result);
        }

        [Fact]
        public void AverageResponseTime_WithMultipleResults_ReturnsAverage()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);

            // Act
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(200));
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(300));

            // Assert
            Assert.Equal(TimeSpan.FromMilliseconds(200), metrics.AverageResponseTime);
        }

        #endregion

        #region FailureRate Tests

        [Fact]
        public void FailureRate_WithNoRequests_ReturnsZero()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);

            // Act
            var result = metrics.FailureRate;

            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public void FailureRate_WithMixedResults_ReturnsCorrectRate()
        {
            // Arrange
            var metrics = new ConnectionHealthMetrics(_timeProvider);

            // Act
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));
            metrics.RecordFailure(new InvalidOperationException(), TimeSpan.FromMilliseconds(100));
            metrics.RecordSuccess(TimeSpan.FromMilliseconds(100));
            metrics.RecordFailure(new InvalidOperationException(), TimeSpan.FromMilliseconds(100));

            // Assert
            Assert.Equal(0.5, metrics.FailureRate);
        }

        #endregion
    }
}
