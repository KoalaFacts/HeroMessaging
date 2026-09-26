using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

[Trait("Category", "Unit")]
public class RabbitMqConsumerReentrantShutdownTests
{
    [Fact]
    public async Task Handler_CanStopAndDisposeItsOwnConsumerBeforeAcknowledgement()
    {
        var channel = new Mock<IChannel>();
        IAsyncBasicConsumer? basicConsumer = null;
        var operations = new List<string>();
        channel.Setup(ch => ch.IsOpen).Returns(true);
        channel.Setup(ch => ch.BasicQosAsync(
            It.IsAny<uint>(), It.IsAny<ushort>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        channel.Setup(ch => ch.BasicConsumeAsync(
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<bool>(), It.IsAny<IDictionary<string, object?>>(),
            It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object?>, IAsyncBasicConsumer, CancellationToken>(
                (_, _, _, _, _, _, consumer, _) => basicConsumer = consumer)
            .ReturnsAsync("test-tag");
        channel.Setup(ch => ch.BasicCancelAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(() => basicConsumer!.HandleBasicCancelOkAsync("test-tag"));
        channel.Setup(ch => ch.BasicAckAsync(1, false, It.IsAny<CancellationToken>()))
            .Callback(() => operations.Add("ack"))
            .Returns(ValueTask.CompletedTask);
        channel.Setup(ch => ch.CloseAsync(
            It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => operations.Add("close"))
            .Returns(Task.CompletedTask);

        var host = new Mock<IRabbitMqConsumerHost>();
        host.SetupGet(h => h.Name).Returns("RabbitMQ");
        RabbitMqConsumer? consumer = null;
        consumer = new RabbitMqConsumer(
            "test-consumer", TransportAddress.Queue("test-queue"), channel.Object,
            async (_, _, ct) =>
            {
                await consumer!.StopAsync(ct);
                await consumer.DisposeAsync();
            },
            new ConsumerOptions { AutoAcknowledge = true }, host.Object,
            Mock.Of<ILogger<RabbitMqConsumer>>(), TimeProvider.System);

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await basicConsumer!.HandleBasicDeliverAsync(
            "test-tag", 1, false, string.Empty, "test-queue",
            new BasicProperties { MessageId = "message-1" }, new byte[] { 1 },
            timeout.Token).WaitAsync(timeout.Token);
        await consumer.DisposeAsync().AsTask().WaitAsync(timeout.Token);

        Assert.Equal(["ack", "close"], operations);
    }
}
