using HeroMessaging.Transport.RabbitMQ;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class FaultHeadersTests
{
    private static readonly DateTimeOffset FailedAt = new(2026, 11, 6, 1, 2, 3, TimeSpan.Zero);

    [Fact]
    public void Create_RecordsTheFailureAndKeepsOtherHeaders()
    {
        var headers = new Dictionary<string, object?> { ["tenant"] = "a", ["x-delivery-count"] = 3L };

        var fault = FaultHeaders.Create(headers, "orders", "consumer-1", 2, FailedAt, new InvalidOperationException("boom"), reason: null);

        Assert.Equal("a", fault["tenant"]);
        Assert.Equal("orders", fault[FaultHeaders.Queue]);
        Assert.Equal("consumer-1", fault[FaultHeaders.Consumer]);
        Assert.Equal(2L, fault[FaultHeaders.Retries]);
        Assert.Equal("2026-11-06T01:02:03.0000000Z", fault[FaultHeaders.Time]);
        Assert.Equal("System.InvalidOperationException", fault[FaultHeaders.ExceptionType]);
        Assert.Equal("boom", fault[FaultHeaders.ExceptionMessage]);
        Assert.False(fault.ContainsKey(FaultHeaders.Reason));
        Assert.False(fault.ContainsKey("x-delivery-count"));
        Assert.True(headers.ContainsKey("x-delivery-count"), "The delivered headers must not be modified.");
    }

    [Fact]
    public void Create_WithReasonAndNoException_RecordsTheReason()
    {
        var fault = FaultHeaders.Create(null, "orders", "consumer-1", 0, FailedAt, exception: null, reason: "invalid payload");

        Assert.Equal("invalid payload", fault[FaultHeaders.Reason]);
        Assert.False(fault.ContainsKey(FaultHeaders.ExceptionType));
        Assert.False(fault.ContainsKey(FaultHeaders.ExceptionMessage));
    }

    [Fact]
    public void Create_ReplacesEarlierFaultContext()
    {
        var headers = new Dictionary<string, object?>
        {
            [FaultHeaders.Queue] = "old",
            [FaultHeaders.Reason] = "old reason",
            [FaultHeaders.ExceptionType] = "Old"
        };

        var fault = FaultHeaders.Create(headers, "orders", "consumer-1", 1, FailedAt, new TimeoutException("late"), reason: null);

        Assert.Equal("orders", fault[FaultHeaders.Queue]);
        Assert.Equal("System.TimeoutException", fault[FaultHeaders.ExceptionType]);
        Assert.False(fault.ContainsKey(FaultHeaders.Reason));
    }

    [Fact]
    public void Create_TruncatesLongExceptionMessages()
    {
        var fault = FaultHeaders.Create(null, "orders", "consumer-1", 0, FailedAt,
            new InvalidOperationException(new string('x', FaultHeaders.MaxMessageLength + 10)), reason: null);

        Assert.Equal(FaultHeaders.MaxMessageLength, ((string)fault[FaultHeaders.ExceptionMessage]!).Length);
    }

    [Fact]
    public void Truncate_AtASurrogatePair_KeepsThePairWhole()
    {
        var message = new string('x', FaultHeaders.MaxMessageLength - 1) + "\U0001F600" + "tail";

        var truncated = FaultHeaders.Truncate(message);

        Assert.Equal(FaultHeaders.MaxMessageLength - 1, truncated.Length);
        Assert.False(char.IsHighSurrogate(truncated[^1]));
    }
}
