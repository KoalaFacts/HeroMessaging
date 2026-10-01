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
    }

    [Theory]
    [InlineData(nameof(InProcessBenchmarkEvents.BatchStart), 1, 3)]
    [InlineData(nameof(InProcessBenchmarkEvents.BatchStop), 2, 0)]
    public void WireSchemaMatchesBenchmarkDefinition(string name, int eventId, int parameterCount)
    {
        var method = typeof(InProcessBenchmarkEvents).GetMethod(name)!;
        var definition = method.GetCustomAttribute<EventAttribute>()!;
        Assert.Equal(eventId, definition.EventId);
        Assert.Equal(0, definition.Version);
        Assert.Equal(parameterCount, method.GetParameters().Length);
        Assert.All(method.GetParameters(), parameter => Assert.Equal(typeof(int), parameter.ParameterType));
        string[] names = eventId == 1 ? ["messages", "handlers", "producers"] : [];
        Assert.Equal(names, method.GetParameters().Select(parameter => parameter.Name));
    }

    public class Start
    {
        [Fact]
        public void MatchesActualReceiptPayloadWithoutDynamicMetadata()
        {
            var payload = Convert.FromHexString("393C23000300000020000000");
            Assert.True(BatchMarkerPayload.MatchesStart(BatchMarkerPayload.ProviderId, 1, 0, payload, 2309177));
        }

        [Theory]
        [InlineData(2, 0, "393C23000300000020000000", 2309177)]
        [InlineData(1, 1, "393C23000300000020000000", 2309177)]
        [InlineData(1, 0, "", 2309177)]
        [InlineData(1, 0, "393C230003000000200000", 2309177)]
        [InlineData(1, 0, "393C2300030000002000000000", 2309177)]
        [InlineData(1, 0, "393C23000300000020000000", 100000)]
        [InlineData(1, 0, "393C23000100000020000000", 2309177)]
        [InlineData(1, 0, "393C23000300000001000000", 2309177)]
        [InlineData(1, 0, "000000000300000020000000", 0)]
        [InlineData(1, 0, "FFFFFFFF0300000020000000", -1)]
        public void RejectsUnknownOrMismatchedSchema(int eventId, int version, string bytes, int messages)
        {
            Assert.False(BatchMarkerPayload.MatchesStart(BatchMarkerPayload.ProviderId, eventId, version,
                Convert.FromHexString(bytes), messages));
        }

        [Fact]
        public void RejectsUnrelatedProvider()
        {
            Assert.False(BatchMarkerPayload.MatchesStart(Guid.Empty, 1, 0,
                Convert.FromHexString("393C23000300000020000000"), 2309177));
        }
    }

    public class Stop
    {
        [Fact]
        public void MatchesKnownEmptyStop()
        {
            Assert.True(BatchMarkerPayload.MatchesStop(BatchMarkerPayload.ProviderId, 2, 0, []));
        }

        [Theory]
        [InlineData(1, 0, "")]
        [InlineData(2, 1, "")]
        [InlineData(2, 0, "00")]
        public void RejectsUnknownOrMismatchedSchema(int eventId, int version, string bytes)
        {
            Assert.False(BatchMarkerPayload.MatchesStop(BatchMarkerPayload.ProviderId, eventId, version,
                Convert.FromHexString(bytes)));
        }

        [Fact]
        public void RejectsUnrelatedProvider()
        {
            Assert.False(BatchMarkerPayload.MatchesStop(Guid.Empty, 2, 0, []));
        }
    }
}
