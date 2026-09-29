using HeroMessaging.Abstractions;
using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Abstractions.Storage;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Processing;
using HeroMessaging.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

public sealed class ExternalOutboxConcurrencyTests
{
    [Fact]
    public async Task ClaimsStayWithinLimitAndCompletedDeliveriesFreeSlots()
    {
        const int concurrency = 4;
        const int messageCount = concurrency + 1;
        var entries = Enumerable.Range(0, messageCount).Select(_ => new OutboxEntry
        {
            Id = Guid.NewGuid().ToString(),
            Message = TestMessageBuilder.CreateValidMessage(),
            Options = new OutboxOptions { Destination = "orders" }
        }).ToArray();
        var storage = new Mock<IExternalOutboxStorage>();
        storage.SetupGet(value => value.SupportsExternalClaims).Returns(true);
        storage.Setup(value => value.GetLocalPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var claimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var claimCount = 0;
        storage.Setup(value => value.ClaimExternalAsync(1, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var index = Interlocked.Increment(ref claimCount) - 1;
                if (index == concurrency - 1)
                    claimed.TrySetResult();
                return index < entries.Length ? [new OutboxLease(entries[index], Guid.NewGuid())] : [];
            });
        storage.Setup(value => value.RenewExternalAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completionCount = 0;
        storage.Setup(value => value.CompleteExternalAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (Interlocked.Increment(ref completionCount) == messageCount)
                    completed.TrySetResult();
                return true;
            });

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Mock<IConfirmedQueueTransport>();
        transport.SetupGet(value => value.State).Returns(TransportState.Connected);
        transport.Setup(value => value.SendConfirmedAsync(It.IsAny<TransportAddress>(),
                It.IsAny<TransportEnvelope>(), It.IsAny<CancellationToken>()))
            .Returns(async (TransportAddress _, TransportEnvelope _, CancellationToken token) =>
                await release.Task.WaitAsync(token));
        var serializer = new Mock<IMessageSerializer>();
        serializer.SetupGet(value => value.ContentType).Returns("application/custom");
        serializer.Setup(value => value.SerializeAsync(It.IsAny<TestMessage>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<byte[]>("message"u8.ToArray()));

        using var services = new ServiceCollection().BuildServiceProvider();
        await using var processor = new OutboxProcessor(storage.Object, services,
            NullLogger<OutboxProcessor>.Instance, TimeProvider.System, transport.Object, serializer.Object,
            new ProcessingOptions { ExternalOutboxMaxConcurrency = concurrency });
        await processor.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await claimed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
            Assert.Equal(concurrency, Volatile.Read(ref claimCount));

            release.TrySetResult();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(messageCount, Volatile.Read(ref completionCount));
        }
        finally
        {
            release.TrySetResult();
            await processor.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidLimitIsRejected(int limit)
    {
        var storage = new Mock<IExternalOutboxStorage>();
        using var services = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboxProcessor(storage.Object, services,
            NullLogger<OutboxProcessor>.Instance, TimeProvider.System,
            processingOptions: new ProcessingOptions { ExternalOutboxMaxConcurrency = limit }));
    }
}
