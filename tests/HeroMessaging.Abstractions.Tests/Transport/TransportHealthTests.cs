using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Time.Testing;

namespace HeroMessaging.Abstractions.Tests.Transport;

[Trait("Category", "Unit")]
public class TransportHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public class StatusFactories
    {
        [Fact]
        public void HealthyUsesProvidedClockAndConnectedState()
        {
            var health = TransportHealth.Healthy("rabbit", timeProvider: new FakeTimeProvider(Now));

            Assert.Equal(HealthStatus.Healthy, health.Status);
            Assert.Equal(TransportState.Connected, health.State);
            Assert.Equal("rabbit", health.TransportName);
            Assert.Equal(Now, health.Timestamp);
            Assert.Equal(TimeSpan.Zero, health.Duration);
            Assert.Equal(0, health.ActiveConnections);
        }

        [Fact]
        public void DegradedPreservesReasonAndState()
        {
            var health = TransportHealth.Degraded("queue", "slow", TransportState.Disconnected, new FakeTimeProvider(Now));

            Assert.Equal(HealthStatus.Degraded, health.Status);
            Assert.Equal(TransportState.Disconnected, health.State);
            Assert.Equal("slow", health.StatusMessage);
            Assert.Equal(Now, health.Timestamp);
        }

        [Fact]
        public void UnhealthyDefaultsToDisconnected()
        {
            var health = TransportHealth.Unhealthy("queue", "offline", timeProvider: new FakeTimeProvider(Now));

            Assert.Equal(HealthStatus.Unhealthy, health.Status);
            Assert.Equal(TransportState.Disconnected, health.State);
            Assert.Equal("offline", health.StatusMessage);
            Assert.Equal(Now, health.Timestamp);
        }

        [Fact]
        public void ExceptionRecordsFaultAndOccurrenceTime()
        {
            var failure = new InvalidOperationException("connection lost");
            var health = TransportHealth.FromException("queue", failure, new FakeTimeProvider(Now));

            Assert.Equal(HealthStatus.Unhealthy, health.Status);
            Assert.Equal(TransportState.Faulted, health.State);
            Assert.Equal("connection lost", health.StatusMessage);
            Assert.Contains("InvalidOperationException", health.LastError);
            Assert.Equal(Now, health.LastErrorTime);
            Assert.Equal(Now, health.Timestamp);
        }
    }
}
