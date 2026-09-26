using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

[Trait("Category", "Unit")]
public class RabbitMqConsumerRecoveryShutdownTests
{
    [Fact]
    public async Task StopAsync_AfterReregistration_WaitsForNewCancellationConfirmation()
    {
        var channel = new Mock<IChannel>();
        IAsyncBasicConsumer? basicConsumer = null;
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
            .Returns(Task.CompletedTask);
        channel.Setup(ch => ch.CloseAsync(
            It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var host = new Mock<IRabbitMqConsumerHost>();
        host.SetupGet(h => h.Name).Returns("RabbitMQ");
        var consumer = new RabbitMqConsumer(
            "test-consumer", TransportAddress.Queue("test-queue"), channel.Object,
            static (_, _, _) => Task.CompletedTask, new ConsumerOptions(), host.Object,
            Mock.Of<ILogger<RabbitMqConsumer>>(), TimeProvider.System);

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await basicConsumer!.HandleBasicCancelOkAsync("test-tag", TestContext.Current.CancellationToken);
        await basicConsumer.HandleBasicConsumeOkAsync("test-tag", TestContext.Current.CancellationToken);

        var stop = consumer.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(stop.IsCompleted);

        await basicConsumer.HandleBasicCancelOkAsync("test-tag", TestContext.Current.CancellationToken);
        await stop;
        await consumer.DisposeAsync();
    }
}
