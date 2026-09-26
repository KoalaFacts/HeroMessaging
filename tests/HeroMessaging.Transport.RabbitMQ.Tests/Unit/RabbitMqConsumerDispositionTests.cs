using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

[Trait("Category", "Unit")]
public class RabbitMqConsumerDispositionTests
{
    [Theory]
    [InlineData("none", true, true, false, true)]
    [InlineData("none", false, false, false, true)]
    [InlineData("ack", true, true, false, true)]
    [InlineData("ack-twice", true, true, false, true)]
    [InlineData("reject", true, false, true, false)]
    [InlineData("defer", true, false, true, true)]
    [InlineData("deadletter", true, false, true, false)]
    [InlineData("throw", true, false, true, true)]
    [InlineData("throw", false, false, true, false)]
    [InlineData("ack-then-throw", true, true, false, true)]
    public async Task Delivery_HasOnlyOneDisposition(
        string action, bool requeueOnFailure, bool expectedAck, bool expectedNack, bool expectedRequeue)
    {
        var channel = new Mock<IChannel>();
        IAsyncBasicConsumer? basicConsumer = null;
        bool? actualRequeue = null;
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
        channel.Setup(ch => ch.BasicAckAsync(It.IsAny<ulong>(), false, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        channel.Setup(ch => ch.BasicNackAsync(It.IsAny<ulong>(), false, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<ulong, bool, bool, CancellationToken>((_, _, requeue, _) => actualRequeue = requeue)
            .Returns(ValueTask.CompletedTask);
        channel.Setup(ch => ch.BasicCancelAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        channel.Setup(ch => ch.CloseAsync(It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var host = new Mock<IRabbitMqConsumerHost>();
        host.SetupGet(h => h.Name).Returns("RabbitMQ");
        var options = new ConsumerOptions { AutoAcknowledge = action != "none" || expectedAck, RequeueOnFailure = requeueOnFailure };
        await using var consumer = new RabbitMqConsumer(
            "test-consumer", TransportAddress.Queue("test-queue"), channel.Object,
            async (_, context, ct) =>
            {
                switch (action)
                {
                    case "ack":
                    case "ack-then-throw":
                        await context.AcknowledgeAsync(ct);
                        break;
                    case "ack-twice":
                        await context.AcknowledgeAsync(ct);
                        await context.AcknowledgeAsync(ct);
                        break;
                    case "reject":
                        await context.RejectAsync(false, ct);
                        break;
                    case "defer":
                        await context.DeferAsync(cancellationToken: ct);
                        break;
                    case "deadletter":
                        await context.DeadLetterAsync(cancellationToken: ct);
                        break;
                    default:
                        break;
                }

                if (action is "throw" or "ack-then-throw")
                    throw new InvalidOperationException("handler failed");
            }, options, host.Object, Mock.Of<ILogger<RabbitMqConsumer>>(), TimeProvider.System);

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await basicConsumer!.HandleBasicDeliverAsync(
            "test-tag", 1, false, string.Empty, "test-queue",
            new BasicProperties { MessageId = "message-1" }, new byte[] { 1 }, TestContext.Current.CancellationToken);

        channel.Verify(ch => ch.BasicAckAsync(1, false, It.IsAny<CancellationToken>()),
            expectedAck ? Times.Once : Times.Never);
        channel.Verify(ch => ch.BasicNackAsync(1, false, It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            expectedNack ? Times.Once : Times.Never);
        if (expectedNack)
            Assert.Equal(expectedRequeue, actualRequeue);
    }
}
