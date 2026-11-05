using HeroMessaging.Transport.RabbitMQ;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ConsumerRetryAttemptsTests
{
    [Fact]
    public void GetPreviousRetries_UnknownMessage_ReturnsZero()
    {
        var attempts = new ConsumerRetryAttempts();

        Assert.Equal(0, attempts.GetPreviousRetries("m1", headers: null));
    }

    [Fact]
    public void GetPreviousRetries_WithoutMessageIdOrBrokerCount_ReturnsNull()
    {
        var attempts = new ConsumerRetryAttempts();

        Assert.Null(attempts.GetPreviousRetries(string.Empty, headers: null));
    }

    [Theory]
    [InlineData(3L)]
    [InlineData(3)]
    [InlineData("3")]
    public void GetPreviousRetries_BrokerDeliveryCount_IsUsedAsFloor(object value)
    {
        var attempts = new ConsumerRetryAttempts();
        attempts.Increment("m1", 0);
        var headers = new Dictionary<string, object?> { ["x-delivery-count"] = value };

        Assert.Equal(3, attempts.GetPreviousRetries("m1", headers));
        Assert.Equal(3, attempts.GetPreviousRetries(string.Empty, headers));
    }

    [Fact]
    public void GetPreviousRetries_BrokerDeliveryCountAsBytes_IsParsed()
    {
        var attempts = new ConsumerRetryAttempts();
        var headers = new Dictionary<string, object?> { ["x-delivery-count"] = "4"u8.ToArray() };

        Assert.Equal(4, attempts.GetPreviousRetries("m1", headers));
    }

    [Fact]
    public void Increment_RepeatedForSameMessage_CountsEachRetry()
    {
        var attempts = new ConsumerRetryAttempts();

        Assert.Equal(1, attempts.Increment("m1", 0));
        Assert.Equal(2, attempts.Increment("m1", 0));
        Assert.Equal(5, attempts.Increment("m1", 4));
        Assert.Equal(5, attempts.GetPreviousRetries("m1", headers: null));
    }

    [Fact]
    public void Clear_RemovesCount()
    {
        var attempts = new ConsumerRetryAttempts();
        attempts.Increment("m1", 0);

        attempts.Clear("m1");

        Assert.Equal(0, attempts.GetPreviousRetries("m1", headers: null));
        Assert.Equal(0, attempts.Count);
    }

    [Fact]
    public void Increment_AtCapacity_EvictsLeastRecentlyRecorded()
    {
        var attempts = new ConsumerRetryAttempts(capacity: 2);
        attempts.Increment("a", 0);
        attempts.Increment("b", 0);
        attempts.Increment("a", 0);

        attempts.Increment("c", 0);

        Assert.Equal(2, attempts.Count);
        Assert.Equal(2, attempts.GetPreviousRetries("a", headers: null));
        Assert.Equal(0, attempts.GetPreviousRetries("b", headers: null));
        Assert.Equal(1, attempts.GetPreviousRetries("c", headers: null));
    }

    [Fact]
    public void Increment_AfterClearAndReRecord_DoesNotLoseLiveCountToStaleEntry()
    {
        var attempts = new ConsumerRetryAttempts(capacity: 2);
        attempts.Increment("a", 0);
        attempts.Clear("a");
        attempts.Increment("b", 0);
        attempts.Increment("a", 0);

        attempts.Increment("a", 0);

        Assert.Equal(2, attempts.GetPreviousRetries("a", headers: null));
        Assert.Equal(1, attempts.GetPreviousRetries("b", headers: null));
        Assert.Equal(2, attempts.Count);
    }
}
