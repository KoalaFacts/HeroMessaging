using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class RabbitMqDeadLetterReplayValidationTests
{
    [Fact]
    public async Task ReplayDeadLettersAsync_WhenDisconnected_ThrowsInvalidOperationException()
    {
        await using var transport = CreateTransport(usePublisherConfirms: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.ReplayDeadLettersAsync(
            TransportAddress.Queue("orders-dlq"), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplayDeadLettersAsync_WithoutPublisherConfirms_ThrowsBeforeTouchingTheBroker()
    {
        await using var transport = CreateTransport(usePublisherConfirms: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => transport.ReplayDeadLettersAsync(
            TransportAddress.Queue("orders-dlq"), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(nameof(RabbitMqTransportOptions.UsePublisherConfirms), error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReplayDeadLettersAsync_WithNonPositiveMaxMessages_ThrowsArgumentOutOfRange(int maxMessages)
    {
        await using var transport = CreateTransport(usePublisherConfirms: true);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => transport.ReplayDeadLettersAsync(
            TransportAddress.Queue("orders-dlq"),
            new DeadLetterReplayOptions { MaxMessages = maxMessages },
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ReplayDeadLettersAsync_WithBlankTargetQueue_ThrowsArgumentException(string target)
    {
        await using var transport = CreateTransport(usePublisherConfirms: true);

        await Assert.ThrowsAsync<ArgumentException>(() => transport.ReplayDeadLettersAsync(
            TransportAddress.Queue("orders-dlq"),
            new DeadLetterReplayOptions { TargetQueue = target },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplayDeadLettersAsync_WithTargetEqualToTheDeadLetterQueue_ThrowsArgumentException()
    {
        await using var transport = CreateTransport(usePublisherConfirms: true);

        await Assert.ThrowsAsync<ArgumentException>(() => transport.ReplayDeadLettersAsync(
            TransportAddress.Queue("orders-dlq"),
            new DeadLetterReplayOptions { TargetQueue = "orders-dlq" },
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ReplayDeadLettersAsync_WithBlankDeadLetterQueue_ThrowsArgumentException(string queue)
    {
        await using var transport = CreateTransport(usePublisherConfirms: true);

        await Assert.ThrowsAsync<ArgumentException>(() => transport.ReplayDeadLettersAsync(
            new TransportAddress(queue), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DeadLetterReplayOptions_Defaults_ReplayUpToOneHundredToTheSourceQueue()
    {
        var options = new DeadLetterReplayOptions();

        Assert.Equal(100, options.MaxMessages);
        Assert.Null(options.TargetQueue);
    }

    private static RabbitMqTransport CreateTransport(bool usePublisherConfirms) =>
        new(new RabbitMqTransportOptions { UsePublisherConfirms = usePublisherConfirms }, NullLoggerFactory.Instance, TimeProvider.System);
}
