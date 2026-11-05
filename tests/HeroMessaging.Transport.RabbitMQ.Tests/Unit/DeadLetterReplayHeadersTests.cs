using System.Text;
using HeroMessaging.Transport.RabbitMQ;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class DeadLetterReplayHeadersTests
{
    [Fact]
    public void ResolveSourceQueue_UsesMostRecentDeathEntry()
    {
        var headers = new Dictionary<string, object?>
        {
            ["x-death"] = new List<object>
            {
                Death("orders", "rejected"),
                Death("orders-retry", "expired")
            }
        };

        Assert.Equal("orders", DeadLetterReplayHeaders.ResolveSourceQueue(headers));
    }

    [Fact]
    public void ResolveSourceQueue_PrefersTheFaultQueueOverDeathHistory()
    {
        var headers = new Dictionary<string, object?>
        {
            ["hero-fault-queue"] = Encoding.UTF8.GetBytes("orders"),
            ["x-death"] = new List<object> { Death("older", "rejected") }
        };

        Assert.Equal("orders", DeadLetterReplayHeaders.ResolveSourceQueue(headers));
    }

    [Fact]
    public void PrepareHeaders_RemovesFaultContext()
    {
        var headers = new Dictionary<string, object?>
        {
            ["hero-fault-queue"] = "orders",
            ["hero-fault-exception-type"] = "System.InvalidOperationException",
            ["tenant"] = "a"
        };

        var prepared = DeadLetterReplayHeaders.PrepareHeaders(headers);

        Assert.DoesNotContain(prepared.Keys, static key => key.StartsWith("hero-fault-", StringComparison.Ordinal));
        Assert.Equal("a", prepared["tenant"]);
    }

    [Fact]
    public void ResolveSourceQueue_AcceptsStringValues()
    {
        var headers = new Dictionary<string, object?>
        {
            ["x-death"] = new List<object>
            {
                new Dictionary<string, object?> { ["queue"] = "orders", ["reason"] = "rejected" }
            }
        };

        Assert.Equal("orders", DeadLetterReplayHeaders.ResolveSourceQueue(headers));
    }

    [Fact]
    public void ResolveSourceQueue_WithoutDeathHistory_FallsBackToFirstDeathQueue()
    {
        var headers = new Dictionary<string, object?> { ["x-first-death-queue"] = Encoding.UTF8.GetBytes("orders") };

        Assert.Equal("orders", DeadLetterReplayHeaders.ResolveSourceQueue(headers));
    }

    [Theory]
    [InlineData("no headers")]
    [InlineData("empty headers")]
    [InlineData("empty history")]
    [InlineData("history not a list")]
    [InlineData("entry without queue")]
    [InlineData("entry with empty queue")]
    public void ResolveSourceQueue_WithoutUsableHistory_ReturnsNull(string scenario)
    {
        Dictionary<string, object?>? headers = scenario switch
        {
            "no headers" => null,
            "empty headers" => [],
            "empty history" => new() { ["x-death"] = new List<object>() },
            "history not a list" => new() { ["x-death"] = 42 },
            "entry without queue" => new() { ["x-death"] = new List<object> { new Dictionary<string, object?> { ["reason"] = "rejected" } } },
            _ => new() { ["x-death"] = new List<object> { Death(string.Empty, "rejected") } }
        };

        Assert.Null(DeadLetterReplayHeaders.ResolveSourceQueue(headers));
    }

    [Fact]
    public void PrepareHeaders_RemovesBrokerDeliveryCountAndKeepsHistory()
    {
        var death = new List<object> { Death("orders", "rejected") };
        var headers = new Dictionary<string, object?>
        {
            ["x-delivery-count"] = 3L,
            ["x-death"] = death,
            ["tenant"] = "a"
        };

        var prepared = DeadLetterReplayHeaders.PrepareHeaders(headers);

        Assert.False(prepared.ContainsKey("x-delivery-count"));
        Assert.Same(death, prepared["x-death"]);
        Assert.Equal("a", prepared["tenant"]);
        Assert.Equal(1L, prepared[DeadLetterReplayHeaders.ReplayCountHeader]);
        Assert.True(headers.ContainsKey("x-delivery-count"), "The delivered headers must not be modified.");
    }

    [Theory]
    [InlineData(2L, 3L)]
    [InlineData(2, 3L)]
    [InlineData("2", 3L)]
    [InlineData("garbage", 1L)]
    public void PrepareHeaders_IncrementsReplayCount(object previous, long expected)
    {
        var headers = new Dictionary<string, object?> { [DeadLetterReplayHeaders.ReplayCountHeader] = previous };

        var prepared = DeadLetterReplayHeaders.PrepareHeaders(headers);

        Assert.Equal(expected, prepared[DeadLetterReplayHeaders.ReplayCountHeader]);
    }

    [Fact]
    public void PrepareHeaders_ReplayCountAsBytes_IsParsed()
    {
        var headers = new Dictionary<string, object?> { [DeadLetterReplayHeaders.ReplayCountHeader] = Encoding.UTF8.GetBytes("4") };

        Assert.Equal(5L, DeadLetterReplayHeaders.PrepareHeaders(headers)[DeadLetterReplayHeaders.ReplayCountHeader]);
    }

    [Fact]
    public void PrepareHeaders_WithoutHeaders_AddsReplayCount()
    {
        var prepared = DeadLetterReplayHeaders.PrepareHeaders(null);

        Assert.Equal(1L, Assert.Single(prepared).Value);
    }

    private static Dictionary<string, object?> Death(string queue, string reason) => new()
    {
        ["queue"] = Encoding.UTF8.GetBytes(queue),
        ["reason"] = Encoding.UTF8.GetBytes(reason),
        ["count"] = 1L
    };
}
