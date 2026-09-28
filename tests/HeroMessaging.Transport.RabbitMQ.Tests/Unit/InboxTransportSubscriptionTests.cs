using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Processing;
using Moq;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class InboxTransportSubscriptionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcknowledgesOnlyAfterInboxAcceptsOrFindsDuplicate(bool inserted)
    {
        var message = new InboundMessage();
        var steps = new List<string>();
        var inbox = new Mock<IInboxProcessor>();
        inbox.Setup(processor => processor.ProcessIncomingAsync(message, It.IsAny<InboxOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                steps.Add("inbox");
                return Task.FromResult(inserted);
            });
        var (transport, serializer, getHandler, getOptions) = CreateSubscription(message);

        await InboxTransportSubscription.SubscribeAsync<InboundMessage>(transport.Object,
            TransportAddress.Queue("inbox"), inbox.Object, serializer.Object,
            cancellationToken: TestContext.Current.CancellationToken);

        var options = getOptions();
        Assert.NotNull(options);
        Assert.False(options.AutoAcknowledge);
        Assert.True(options.RequeueOnFailure);
        await getHandler()(Envelope(message.MessageId), new MessageContext
        {
            Acknowledge = _ =>
            {
                steps.Add("ack");
                return Task.CompletedTask;
            }
        }, TestContext.Current.CancellationToken);

        Assert.Equal(["inbox", "ack"], steps);
    }

    [Fact]
    public async Task InboxFailureLeavesDeliveryUnacknowledgedForRequeue()
    {
        var message = new InboundMessage();
        var inbox = new Mock<IInboxProcessor>();
        inbox.Setup(processor => processor.ProcessIncomingAsync(message, It.IsAny<InboxOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var (transport, serializer, getHandler, _) = CreateSubscription(message);
        await InboxTransportSubscription.SubscribeAsync<InboundMessage>(transport.Object,
            TransportAddress.Queue("inbox"), inbox.Object, serializer.Object,
            cancellationToken: TestContext.Current.CancellationToken);
        var acknowledged = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => getHandler()(Envelope(message.MessageId),
            new MessageContext { Acknowledge = _ => { acknowledged = true; return Task.CompletedTask; } },
            TestContext.Current.CancellationToken));

        Assert.False(acknowledged);
    }

    [Fact]
    public async Task MismatchedMessageIdIsDeadLetteredWithoutEnteringInbox()
    {
        var message = new InboundMessage();
        var inbox = new Mock<IInboxProcessor>();
        var (transport, serializer, getHandler, _) = CreateSubscription(message);
        await InboxTransportSubscription.SubscribeAsync<InboundMessage>(transport.Object,
            TransportAddress.Queue("inbox"), inbox.Object, serializer.Object,
            cancellationToken: TestContext.Current.CancellationToken);
        var acknowledged = false;
        string? deadLetterReason = null;

        await getHandler()(Envelope(Guid.NewGuid()), new MessageContext
        {
            Acknowledge = _ => { acknowledged = true; return Task.CompletedTask; },
            DeadLetter = (reason, _) => { deadLetterReason = reason; return Task.CompletedTask; }
        }, TestContext.Current.CancellationToken);

        inbox.Verify(processor => processor.ProcessIncomingAsync(It.IsAny<IMessage>(), It.IsAny<InboxOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(acknowledged);
        Assert.Equal("Transport and message body IDs do not match", deadLetterReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidEnvelopeIsDeadLetteredWithoutEnteringInbox(bool invalidId)
    {
        var message = new InboundMessage();
        var inbox = new Mock<IInboxProcessor>();
        var (transport, serializer, getHandler, _) = CreateSubscription(message);
        await InboxTransportSubscription.SubscribeAsync<InboundMessage>(transport.Object,
            TransportAddress.Queue("inbox"), inbox.Object, serializer.Object,
            cancellationToken: TestContext.Current.CancellationToken);
        var envelope = invalidId
            ? Envelope(message.MessageId) with { MessageId = "not-a-guid" }
            : Envelope(message.MessageId) with { ContentType = "text/plain" };
        string? deadLetterReason = null;

        await getHandler()(envelope, new MessageContext
        {
            DeadLetter = (reason, _) => { deadLetterReason = reason; return Task.CompletedTask; }
        }, TestContext.Current.CancellationToken);

        Assert.NotNull(deadLetterReason);
        inbox.Verify(processor => processor.ProcessIncomingAsync(It.IsAny<IMessage>(), It.IsAny<InboxOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CorruptPayloadIsDeadLetteredWithoutRequeue()
    {
        var message = new InboundMessage();
        var inbox = new Mock<IInboxProcessor>();
        var (transport, serializer, getHandler, _) = CreateSubscription(message);
        serializer.Setup(value => value.DeserializeAsync<InboundMessage>(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Throws(new FormatException("corrupt payload"));
        await InboxTransportSubscription.SubscribeAsync<InboundMessage>(transport.Object,
            TransportAddress.Queue("inbox"), inbox.Object, serializer.Object,
            cancellationToken: TestContext.Current.CancellationToken);
        string? deadLetterReason = null;

        await getHandler()(Envelope(message.MessageId), new MessageContext
        {
            DeadLetter = (reason, _) => { deadLetterReason = reason; return Task.CompletedTask; }
        }, TestContext.Current.CancellationToken);

        Assert.Equal("Invalid message payload", deadLetterReason);
        inbox.Verify(processor => processor.ProcessIncomingAsync(It.IsAny<IMessage>(), It.IsAny<InboxOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (Mock<IMessageTransport> Transport, Mock<IMessageSerializer> Serializer,
        Func<Func<TransportEnvelope, MessageContext, CancellationToken, Task>> Handler,
        Func<ConsumerOptions?> Options) CreateSubscription(InboundMessage message)
    {
        Func<TransportEnvelope, MessageContext, CancellationToken, Task>? handler = null;
        ConsumerOptions? options = null;
        var transport = new Mock<IMessageTransport>();
        transport.Setup(value => value.SubscribeAsync(It.IsAny<TransportAddress>(),
                It.IsAny<Func<TransportEnvelope, MessageContext, CancellationToken, Task>>(),
                It.IsAny<ConsumerOptions>(), It.IsAny<CancellationToken>()))
            .Callback<TransportAddress, Func<TransportEnvelope, MessageContext, CancellationToken, Task>, ConsumerOptions, CancellationToken>(
                (_, callback, consumerOptions, _) => { handler = callback; options = consumerOptions; })
            .ReturnsAsync(Mock.Of<ITransportConsumer>());
        var serializer = new Mock<IMessageSerializer>();
        serializer.SetupGet(value => value.ContentType).Returns("application/json");
        serializer.Setup(value => value.DeserializeAsync<InboundMessage>(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        return (transport, serializer, () => handler!, () => options);
    }

    private static TransportEnvelope Envelope(Guid messageId) => new()
    {
        MessageId = messageId.ToString(),
        ContentType = "application/json",
        Body = new byte[] { 1 }
    };

    public sealed class InboundMessage : IMessage
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
