using System.Diagnostics.Tracing;
using System.Reflection;
using HeroMessaging.Benchmarks;
using Xunit;

namespace HeroMessaging.Tests.Unit.Benchmarks;

[Trait("Category", "Unit")]
public class BatchMarkerPayloadTests
{
    [Fact]
    public void ProviderMatchesBenchmarkEventSource()
    {
        Assert.Equal(EventSource.GetGuid(typeof(InProcessBenchmarkEvents)), BatchMarkerPayload.ProviderId);
        Assert.Equal(InProcessBenchmarkEvents.SchemaVersion, BatchMarkerPayload.SchemaVersion);
    }

    [Theory]
    [InlineData(nameof(InProcessBenchmarkEvents.BatchStart), 1, 5)]
    [InlineData(nameof(InProcessBenchmarkEvents.BatchStop), 2, 2)]
    public void WireSchemaMatchesBenchmarkDefinition(string name, int eventId, int parameterCount)
    {
        var method = typeof(InProcessBenchmarkEvents).GetMethod(name)!;
        var definition = method.GetCustomAttribute<EventAttribute>()!;
        Assert.Equal(eventId, definition.EventId);
        Assert.Equal(2, definition.Version);
        Assert.Equal(parameterCount, method.GetParameters().Length);
        Assert.All(method.GetParameters(), parameter => Assert.Equal(typeof(int), parameter.ParameterType));
        string[] names = eventId == 1 ? ["schemaVersion", "messages", "handlers", "producers", "batchId"] : ["schemaVersion", "batchId"];
        Assert.Equal(names, method.GetParameters().Select(parameter => parameter.Name));
    }

    [Fact]
    public void EventSourceEmitsVersionedIntegerBatchIdentities()
    {
        using var listener = new MarkerListener();
        InProcessBenchmarkEvents.Log.BatchStart(InProcessBenchmarkEvents.SchemaVersion, 2309177, 3, 32, 2);
        InProcessBenchmarkEvents.Log.BatchStop(InProcessBenchmarkEvents.SchemaVersion, 2);
        Assert.Collection(listener.Events,
            start =>
            {
                Assert.Equal(1, start.EventId);
                Assert.Equal(2, start.Version);
                Assert.Equal<object?>([2, 2309177, 3, 32, 2], start.Payload);
            },
            stop =>
            {
                Assert.Equal(2, stop.EventId);
                Assert.Equal(2, stop.Version);
                Assert.Equal<object?>([2, 2], stop.Payload);
            });
    }

    private sealed class MarkerListener : EventListener
    {
        internal List<EventWrittenEventArgs> Events { get; } = [];

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Guid == BatchMarkerPayload.ProviderId)
                EnableEvents(eventSource, EventLevel.Informational);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData) => Events.Add(eventData);
    }

    public class Start
    {
        [Fact]
        public void ReadsBatchIdentityWithoutDynamicMetadata()
        {
            Assert.True(BatchMarkerPayload.TryReadStart(BatchMarkerPayload.ProviderId, 1,
                Convert.FromHexString("02000000393C2300030000002000000002000000"), 2309177, out var batchId));
            Assert.Equal(2, batchId);
        }

        [Theory]
        [InlineData(2, "02000000393C2300030000002000000002000000", 2309177)]
        [InlineData(1, "393C23000300000020000000", 2309177)]
        [InlineData(1, "393C2300030000002000000002000000", 2309177)]
        [InlineData(1, "00000000393C2300030000002000000002000000", 2309177)]
        [InlineData(1, "01000000393C2300030000002000000002000000", 2309177)]
        [InlineData(1, "03000000393C2300030000002000000002000000", 2309177)]
        [InlineData(1, "", 2309177)]
        [InlineData(1, "02000000393C23000300000020000000020000", 2309177)]
        [InlineData(1, "02000000393C230003000000200000000200000000", 2309177)]
        [InlineData(1, "02000000393C2300030000002000000002000000", 100000)]
        [InlineData(1, "02000000393C2300010000002000000002000000", 2309177)]
        [InlineData(1, "02000000393C2300030000000100000002000000", 2309177)]
        [InlineData(1, "0200000000000000030000002000000002000000", 0)]
        [InlineData(1, "02000000FFFFFFFF030000002000000002000000", -1)]
        [InlineData(1, "02000000393C2300030000002000000000000000", 2309177)]
        [InlineData(1, "02000000393C23000300000020000000FFFFFFFF", 2309177)]
        public void RejectsUnknownOrMismatchedSchema(int eventId, string bytes, int messages)
        {
            Assert.False(BatchMarkerPayload.TryReadStart(BatchMarkerPayload.ProviderId, eventId,
                Convert.FromHexString(bytes), messages, out _));
        }

        [Fact]
        public void RejectsUnrelatedProvider()
        {
            Assert.False(BatchMarkerPayload.TryReadStart(Guid.Empty, 1,
                Convert.FromHexString("02000000393C2300030000002000000002000000"), 2309177, out _));
        }
    }

    public class Stop
    {
        [Fact]
        public void ReadsBatchIdentity()
        {
            Assert.True(BatchMarkerPayload.TryReadStop(BatchMarkerPayload.ProviderId, 2,
                Convert.FromHexString("0200000002000000"), out var batchId));
            Assert.Equal(2, batchId);
        }

        [Theory]
        [InlineData(1, "0200000002000000")]
        [InlineData(2, "")]
        [InlineData(2, "02000000")]
        [InlineData(2, "0000000002000000")]
        [InlineData(2, "0100000002000000")]
        [InlineData(2, "0300000002000000")]
        [InlineData(2, "02000000020000")]
        [InlineData(2, "020000000200000000")]
        [InlineData(2, "0200000000000000")]
        [InlineData(2, "02000000FFFFFFFF")]
        public void RejectsUnknownOrMismatchedSchema(int eventId, string bytes)
        {
            Assert.False(BatchMarkerPayload.TryReadStop(BatchMarkerPayload.ProviderId, eventId,
                Convert.FromHexString(bytes), out _));
        }

        [Fact]
        public void RejectsUnrelatedProvider()
        {
            Assert.False(BatchMarkerPayload.TryReadStop(Guid.Empty, 2, Convert.FromHexString("0200000002000000"), out _));
        }
    }
}
