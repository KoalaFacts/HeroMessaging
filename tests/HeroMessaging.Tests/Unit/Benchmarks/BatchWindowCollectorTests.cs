using System.Buffers.Binary;
using HeroMessaging.Benchmarks;
using Xunit;

namespace HeroMessaging.Tests.Unit.Benchmarks;

[Trait("Category", "Unit")]
public class BatchWindowCollectorTests
{
    private static void Start(BatchWindowCollector collector, int batchId, double timestamp)
    {
        Span<byte> payload = stackalloc byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(payload, BatchMarkerPayload.SchemaVersion);
        BinaryPrimitives.WriteInt32LittleEndian(payload[4..], 500000);
        BinaryPrimitives.WriteInt32LittleEndian(payload[8..], 3);
        BinaryPrimitives.WriteInt32LittleEndian(payload[12..], 32);
        BinaryPrimitives.WriteInt32LittleEndian(payload[16..], batchId);
        collector.Observe(BatchMarkerPayload.ProviderId, 1, payload, 500000, timestamp);
    }

    private static void Stop(BatchWindowCollector collector, int batchId, double timestamp)
    {
        Span<byte> payload = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(payload, BatchMarkerPayload.SchemaVersion);
        BinaryPrimitives.WriteInt32LittleEndian(payload[4..], batchId);
        collector.Observe(BatchMarkerPayload.ProviderId, 2, payload, 500000, timestamp);
    }

    public class Pairing
    {
        [Fact]
        public void AcceptsOnlyMatchingIdentity()
        {
            var collector = new BatchWindowCollector();
            Start(collector, 2, 16000);
            Stop(collector, 2, 29000);
            Assert.Equal(new BatchWindow(2, 16000, 29000), Assert.Single(collector.Windows));
        }

        [Fact]
        public void MissingIntermediateMarkersCannotCreateCrossBatchWindow()
        {
            var collector = new BatchWindowCollector();
            Start(collector, 1, 1600);
            Stop(collector, 1, 15300);
            Start(collector, 2, 15900);
            // Missing stop 2 and start 3 previously produced a false 27-second window.
            Stop(collector, 3, 43300);
            Assert.Equal(new BatchWindow(1, 1600, 15300), Assert.Single(collector.Windows));
        }

        [Fact]
        public void ANewStartInvalidatesAnUnclosedBatch()
        {
            var collector = new BatchWindowCollector();
            Start(collector, 1, 1000);
            Start(collector, 2, 16000);
            Stop(collector, 2, 29000);
            Assert.Equal(2, Assert.Single(collector.Windows).BatchId);
        }

        [Fact]
        public void WarmupIsExcludedEvenWithMeasuredMessageCount()
        {
            var collector = new BatchWindowCollector();
            Start(collector, 0, 1000);
            Stop(collector, 0, 14000);
            Assert.Empty(collector.Windows);
        }

        [Fact]
        public void OrphanAndRepeatedStopsCannotProduceAnotherWindow()
        {
            var collector = new BatchWindowCollector();
            Stop(collector, 1, 500);
            Start(collector, 2, 1000);
            Stop(collector, 2, 14000);
            Stop(collector, 2, 28000);
            Assert.Single(collector.Windows);
        }

        [Fact]
        public void DuplicateIdentityInvalidatesPreviouslyAcceptedWindow()
        {
            var collector = new BatchWindowCollector();
            Start(collector, 1, 1000);
            Stop(collector, 1, 14000);
            Start(collector, 1, 15000);
            Stop(collector, 1, 28000);
            Assert.Empty(collector.Windows);
        }

        [Fact]
        public void OutOfOrderStopCannotBeReusedAfterAStart()
        {
            var collector = new BatchWindowCollector();
            Stop(collector, 2, 1000);
            Start(collector, 2, 2000);
            Stop(collector, 2, 15000);
            Assert.Empty(collector.Windows);
        }
    }

    public class InvalidMarkers
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void MalformedMarkerInvalidatesPendingWindow(int eventId)
        {
            var collector = new BatchWindowCollector();
            Start(collector, 1, 1000);
            collector.Observe(BatchMarkerPayload.ProviderId, eventId, [], 500000, 2000);
            Stop(collector, 1, 14000);
            Assert.Empty(collector.Windows);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(-1)]
        [InlineData(1000)]
        [InlineData(500)]
        public void RejectsNonFiniteOrNonIncreasingTimestamps(double timestamp)
        {
            var collector = new BatchWindowCollector();
            Start(collector, 1, 1000);
            Stop(collector, 1, timestamp);
            Assert.Empty(collector.Windows);
        }

        [Fact]
        public void UnrelatedProviderCannotCloseOrInterruptBatch()
        {
            var collector = new BatchWindowCollector();
            Start(collector, 1, 1000);
            collector.Observe(Guid.Empty, 2, [], 500000, 2000);
            Stop(collector, 1, 14000);
            Assert.Single(collector.Windows);
        }
    }
}
