using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.RabbitMQ;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

[Trait("Category", "Unit")]
public class RabbitMqConsumerShutdownTests
{
    [Fact]
    public async Task StopAsync_DuringStart_CancelsConsumerAfterRegistration()
    {
        var channel = new Mock<IChannel>();
        IAsyncBasicConsumer? basicConsumer = null;
        var registrationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRegistration = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        channel.Setup(ch => ch.IsOpen).Returns(true);
        channel.Setup(ch => ch.BasicQosAsync(
            It.IsAny<uint>(), It.IsAny<ushort>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        channel.Setup(ch => ch.BasicConsumeAsync(
            It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<bool>(), It.IsAny<IDictionary<string, object?>>(),
            It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
            .Callback<string, bool, string, bool, bool, IDictionary<string, object?>, IAsyncBasicConsumer, CancellationToken>(
                (_, _, _, _, _, _, consumer, _) =>
                {
                    basicConsumer = consumer;
                    registrationStarted.SetResult();
                })
            .Returns(() => releaseRegistration.Task);
        channel.Setup(ch => ch.BasicCancelAsync("test-tag", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(() => basicConsumer!.HandleBasicCancelOkAsync("test-tag"));
        channel.Setup(ch => ch.CloseAsync(
            It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var host = new Mock<IRabbitMqConsumerHost>();
        host.SetupGet(h => h.Name).Returns("RabbitMQ");
        var consumer = new RabbitMqConsumer(
            "test-consumer", TransportAddress.Queue("test-queue"), channel.Object,
            static (_, _, _) => Task.CompletedTask,
            new ConsumerOptions(), host.Object,
            Mock.Of<ILogger<RabbitMqConsumer>>(), TimeProvider.System);

        try
        {
            var start = consumer.StartAsync(TestContext.Current.CancellationToken);
            await registrationStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            var stop = consumer.StopAsync(TestContext.Current.CancellationToken);

            Assert.False(stop.IsCompleted);

            releaseRegistration.SetResult("test-tag");
            await Task.WhenAll(start, stop).WaitAsync(TestContext.Current.CancellationToken);
            channel.Verify(ch => ch.BasicCancelAsync("test-tag", It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            releaseRegistration.TrySetResult("test-tag");
            await consumer.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopAndDispose_WaitForDeliveryDispositionBeforeClosingChannel(bool cancelStopWait)
    {
        var channel = new Mock<IChannel>();
        IAsyncBasicConsumer? basicConsumer = null;
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
            .Callback(cancelCompleted.SetResult)
            .Returns(Task.CompletedTask);
        channel.Setup(ch => ch.BasicAckAsync(It.IsAny<ulong>(), false, It.IsAny<CancellationToken>()))
            .Callback(() => operations.Add("ack"))
            .Returns(ValueTask.CompletedTask);
        channel.Setup(ch => ch.CloseAsync(
            It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => operations.Add("close"))
            .Returns(Task.CompletedTask);

        var host = new Mock<IRabbitMqConsumerHost>();
        host.SetupGet(h => h.Name).Returns("RabbitMQ");
        var consumer = new RabbitMqConsumer(
            "test-consumer", TransportAddress.Queue("test-queue"), channel.Object,
            async (_, _, _) =>
            {
                handlerStarted.SetResult();
                await releaseHandler.Task;
            },
            new ConsumerOptions { AutoAcknowledge = true }, host.Object,
            Mock.Of<ILogger<RabbitMqConsumer>>(), TimeProvider.System);

        try
        {
            using var stopCancellation = new CancellationTokenSource();
            await consumer.StartAsync(TestContext.Current.CancellationToken);
            var delivery = basicConsumer!.HandleBasicDeliverAsync(
                "test-tag", 1, false, string.Empty, "test-queue",
                new BasicProperties { MessageId = "message-1" }, new byte[] { 1 },
                TestContext.Current.CancellationToken);
            await handlerStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            var stop = consumer.StopAsync(stopCancellation.Token);
            await cancelCompleted.Task.WaitAsync(TestContext.Current.CancellationToken);
            if (cancelStopWait)
            {
                stopCancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stop);
                stop = consumer.StopAsync(TestContext.Current.CancellationToken);
            }
            var dispose = consumer.DisposeAsync().AsTask();

            Assert.False(stop.IsCompleted);
            Assert.False(dispose.IsCompleted);
            Assert.Empty(operations);

            releaseHandler.SetResult();
            await delivery.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(stop.IsCompleted);
            Assert.Equal(["ack"], operations);

            await basicConsumer.HandleBasicCancelOkAsync("test-tag", TestContext.Current.CancellationToken);
            await Task.WhenAll(delivery, stop, dispose).WaitAsync(TestContext.Current.CancellationToken);

            Assert.Equal(["ack", "close"], operations);
        }
        finally
        {
            releaseHandler.TrySetResult();
            await consumer.DisposeAsync();
        }
    }
}
