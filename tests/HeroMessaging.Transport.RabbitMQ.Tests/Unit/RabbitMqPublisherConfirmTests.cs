using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

[Trait("Category", "Unit")]
public class RabbitMqPublisherConfirmTests
{
    [Fact]
    public async Task SendConfirmed_WithoutPublisherConfirms_RejectsBeforeConnecting()
    {
        var options = new RabbitMqTransportOptions { UsePublisherConfirms = false };
        var transport = new RabbitMqTransport(options, NullLoggerFactory.Instance, TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.SendConfirmedAsync(
            TransportAddress.Queue("orders"), new TransportEnvelope { MessageId = Guid.NewGuid().ToString() },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Publish_WhenConfirmed_KeepsChannelReusable()
    {
        var options = new RabbitMqTransportOptions { UsePublisherConfirms = true };
        var transport = new RabbitMqTransport(options, NullLoggerFactory.Instance, TimeProvider.System);
        var channel = CreateChannel((_, _) => ValueTask.CompletedTask);

        await transport.PublishMessageAsync(
            channel.Object, "exchange", "key", new BasicProperties(), new byte[] { 1 }, TestContext.Current.CancellationToken);

        channel.Verify(c => c.Dispose(), Times.Never);
    }

    [Fact]
    public async Task Publish_WhenConfirmationTimesOut_DiscardsChannel()
    {
        var options = new RabbitMqTransportOptions
        {
            UsePublisherConfirms = true,
            PublisherConfirmTimeout = TimeSpan.FromMilliseconds(30)
        };
        var transport = new RabbitMqTransport(options, NullLoggerFactory.Instance, TimeProvider.System);
        var channel = CreateChannel((_, token) => new ValueTask(Task.Delay(Timeout.Infinite, token)));

        await Assert.ThrowsAsync<TimeoutException>(() => transport.PublishMessageAsync(
            channel.Object, "exchange", "key", new BasicProperties(), new byte[] { 1 }, TestContext.Current.CancellationToken));

        channel.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Publish_WithoutConfirms_DoesNotApplyConfirmationTimeout()
    {
        var options = new RabbitMqTransportOptions
        {
            UsePublisherConfirms = false,
            PublisherConfirmTimeout = TimeSpan.FromMilliseconds(10)
        };
        var transport = new RabbitMqTransport(options, NullLoggerFactory.Instance, TimeProvider.System);
        var channel = CreateChannel((_, token) => new ValueTask(Task.Delay(50, token)));

        await transport.PublishMessageAsync(
            channel.Object, "exchange", "key", new BasicProperties(), new byte[] { 1 }, TestContext.Current.CancellationToken);

        channel.Verify(c => c.Dispose(), Times.Never);
    }

    [Fact]
    public async Task Publish_WhenCallerCancels_DoesNotReportConfirmTimeout()
    {
        var options = new RabbitMqTransportOptions
        {
            UsePublisherConfirms = true,
            PublisherConfirmTimeout = TimeSpan.FromSeconds(5)
        };
        var transport = new RabbitMqTransport(options, NullLoggerFactory.Instance, TimeProvider.System);
        var channel = CreateChannel((_, token) => new ValueTask(Task.Delay(Timeout.Infinite, token)));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(30);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.PublishMessageAsync(
            channel.Object, "exchange", "key", new BasicProperties(), new byte[] { 1 }, cancellation.Token));

        channel.Verify(c => c.Dispose(), Times.Once);
    }

    private static Mock<IChannel> CreateChannel(Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> publish)
    {
        var channel = new Mock<IChannel>();
        channel.Setup(c => c.BasicPublishAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<BasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, string _, bool _, BasicProperties _, ReadOnlyMemory<byte> body, CancellationToken token) =>
                publish(body, token));
        return channel;
    }
}
