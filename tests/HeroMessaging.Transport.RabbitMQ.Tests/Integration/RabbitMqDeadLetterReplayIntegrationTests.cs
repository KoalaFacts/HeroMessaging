using HeroMessaging.Abstractions.Transport;
using RabbitMQ.Client;
using Xunit;

namespace HeroMessaging.Transport.RabbitMQ.Tests.Integration;

/// <summary>
/// Dead-letter retention and replay against a real RabbitMQ broker.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RabbitMqDeadLetterReplayIntegrationTests : RabbitMqIntegrationTestBase
{
    private readonly Dictionary<string, string> _deadLetterExchanges = [];

    private static readonly ConsumerOptions RejectAtOnce = new()
    {
        MessageRetryPolicy = new RetryPolicy { MaxAttempts = 0 }
    };

    [Fact]
    public async Task ReplayDeadLetters_MovesRetainedMessagesBackToTheirSourceQueue()
    {
        var token = TestContext.Current.CancellationToken;
        var (workQueue, deadLetterQueue) = await CreateDeadLetteredQueueAsync();
        var sent = new[] { CreateTestEnvelope("one"), CreateTestEnvelope("two"), CreateTestEnvelope("three") };
        await DeadLetterAsync(workQueue, deadLetterQueue, sent);

        var received = new List<TransportEnvelope>();
        var allReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var consumer = await Transport!.SubscribeAsync(TransportAddress.Queue(workQueue), (envelope, _, _) =>
        {
            lock (received)
            {
                received.Add(envelope);
                if (received.Count == sent.Length)
                    allReceived.TrySetResult();
            }
            return Task.CompletedTask;
        }, cancellationToken: token);

        var result = await Transport.ReplayDeadLettersAsync(TransportAddress.Queue(deadLetterQueue), cancellationToken: token);

        await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Equal(3, result.Replayed);
        Assert.Equal(0, result.Unresolved);
        Assert.Equal(0u, await GetMessageCountAsync(deadLetterQueue));
        lock (received)
        {
            Assert.Equal(sent.Select(static e => e.MessageId).Order(), received.Select(static e => e.MessageId).Order());
            foreach (var envelope in received)
            {
                var original = sent.Single(e => e.MessageId == envelope.MessageId);
                Assert.Equal(original.Body.ToArray(), envelope.Body.ToArray());
                Assert.Equal(original.CorrelationId, envelope.CorrelationId);
                Assert.Equal(original.ContentType, envelope.ContentType);
                Assert.Equal(0, envelope.DeliveryCount);
                Assert.Equal(1L, envelope.Headers[DeadLetterReplayHeaders.ReplayCountHeader]);
                Assert.True(envelope.Headers.ContainsKey("x-death"), "Failure history must survive replay.");
                Assert.True(envelope.Headers.ContainsKey("TestHeader"));
            }
        }
    }

    [Fact]
    public async Task ReplayDeadLetters_BetweenQuorumQueuesAfterAFailedReplay_StartsWithAFreshRetryBudget()
    {
        var token = TestContext.Current.CancellationToken;
        var (workQueue, deadLetterQueue) = await CreateDeadLetteredQueueAsync(quorum: true);
        var retryTwice = new ConsumerOptions
        {
            MessageRetryPolicy = new RetryPolicy { MaxAttempts = 2, InitialDelay = TimeSpan.FromMilliseconds(20) }
        };
        var firstRun = new List<int>();
        var deadLettered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var failing = await Transport!.SubscribeAsync(TransportAddress.Queue(workQueue), (envelope, _, _) =>
        {
            lock (firstRun)
                firstRun.Add(envelope.DeliveryCount);
            throw new InvalidOperationException("Simulated poison message");
        }, retryTwice, token))
        {
            await Transport.SendAsync(TransportAddress.Queue(workQueue), CreateTestEnvelope(), token);
            await WaitForMessageCountAsync(deadLetterQueue, 1);
        }

        var secondRun = new List<int>();
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var recovered = await Transport.SubscribeAsync(TransportAddress.Queue(workQueue), (envelope, _, _) =>
        {
            lock (secondRun)
            {
                secondRun.Add(envelope.DeliveryCount);
                if (secondRun.Count == 2)
                {
                    processed.TrySetResult();
                    return Task.CompletedTask;
                }
            }
            throw new InvalidOperationException("Fails once after the fix is deployed");
        }, retryTwice, token);

        // A failed replay returns the message to the quorum dead-letter queue, which then counts a redelivery.
        await Assert.ThrowsAsync<QueueDeliveryException>(() => Transport.ReplayDeadLettersAsync(
            TransportAddress.Queue(deadLetterQueue),
            new DeadLetterReplayOptions { TargetQueue = CreateQueueName() },
            token));
        await WaitForMessageCountAsync(deadLetterQueue, 1);
        var result = await Transport.ReplayDeadLettersAsync(TransportAddress.Queue(deadLetterQueue), cancellationToken: token);

        await processed.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Equal(1, result.Replayed);
        lock (firstRun)
            Assert.Equal([0, 1, 2], firstRun);
        lock (secondRun)
            Assert.Equal([0, 1], secondRun);
        Assert.Equal(0u, await GetMessageCountAsync(deadLetterQueue));
    }

    [Fact]
    public async Task ReplayDeadLetters_WithMaxMessages_LeavesTheRestRetained()
    {
        var token = TestContext.Current.CancellationToken;
        var (workQueue, deadLetterQueue) = await CreateDeadLetteredQueueAsync();
        await DeadLetterAsync(workQueue, deadLetterQueue, [CreateTestEnvelope(), CreateTestEnvelope(), CreateTestEnvelope()]);

        var result = await Transport!.ReplayDeadLettersAsync(
            TransportAddress.Queue(deadLetterQueue),
            new DeadLetterReplayOptions { MaxMessages = 2 },
            token);

        Assert.Equal(2, result.Replayed);
        Assert.Equal(1u, await GetMessageCountAsync(deadLetterQueue));
        Assert.Equal(2u, await GetMessageCountAsync(workQueue));
    }

    [Fact]
    public async Task ReplayDeadLetters_WithTargetQueue_SendsThereInstead()
    {
        var token = TestContext.Current.CancellationToken;
        var (workQueue, deadLetterQueue) = await CreateDeadLetteredQueueAsync();
        var parkingQueue = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = parkingQueue, Durable = true });
        await Transport!.ConfigureTopologyAsync(topology, token);
        await DeadLetterAsync(workQueue, deadLetterQueue, [CreateTestEnvelope()]);

        var result = await Transport.ReplayDeadLettersAsync(
            TransportAddress.Queue(deadLetterQueue),
            new DeadLetterReplayOptions { TargetQueue = parkingQueue },
            token);

        Assert.Equal(1, result.Replayed);
        Assert.Equal(1u, await GetMessageCountAsync(parkingQueue));
        Assert.Equal(0u, await GetMessageCountAsync(workQueue));
        Assert.Equal(0u, await GetMessageCountAsync(deadLetterQueue));
    }

    [Fact]
    public async Task ReplayDeadLetters_WhenTargetQueueIsMissing_KeepsTheMessageRetained()
    {
        var token = TestContext.Current.CancellationToken;
        var (workQueue, deadLetterQueue) = await CreateDeadLetteredQueueAsync();
        await DeadLetterAsync(workQueue, deadLetterQueue, [CreateTestEnvelope(), CreateTestEnvelope()]);
        var missing = CreateQueueName();

        var failure = await Assert.ThrowsAsync<QueueDeliveryException>(() => Transport!.ReplayDeadLettersAsync(
            TransportAddress.Queue(deadLetterQueue),
            new DeadLetterReplayOptions { TargetQueue = missing },
            token));

        Assert.Equal(missing, failure.Destination);
        Assert.Equal(2u, await WaitForMessageCountAsync(deadLetterQueue, 2));

        // The transport stays usable and a corrected replay succeeds.
        var retry = await Transport!.ReplayDeadLettersAsync(TransportAddress.Queue(deadLetterQueue), cancellationToken: token);
        Assert.Equal(2, retry.Replayed);
    }

    [Fact]
    public async Task ReplayDeadLetters_WithoutDeathHistory_LeavesMessageRetainedAndReportsIt()
    {
        var token = TestContext.Current.CancellationToken;
        var (workQueue, deadLetterQueue) = await CreateDeadLetteredQueueAsync();
        await DeadLetterAsync(workQueue, deadLetterQueue, [CreateTestEnvelope()]);
        await Transport!.SendAsync(TransportAddress.Queue(deadLetterQueue), CreateTestEnvelope("placed by hand"), token);
        await WaitForMessageCountAsync(deadLetterQueue, 2);

        var result = await Transport.ReplayDeadLettersAsync(TransportAddress.Queue(deadLetterQueue), cancellationToken: token);

        Assert.Equal(1, result.Replayed);
        Assert.Equal(1, result.Unresolved);
        Assert.Equal(1u, await GetMessageCountAsync(workQueue));
        Assert.Equal(1u, await WaitForMessageCountAsync(deadLetterQueue, 1));
    }

    [Fact]
    public async Task ReplayDeadLetters_WhenSourceQueueWasDeleted_LeavesItRetainedAndReplaysTheRest()
    {
        var token = TestContext.Current.CancellationToken;
        var (deletedQueue, deadLetterQueue) = await CreateDeadLetteredQueueAsync();
        var otherQueue = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddQueue(new QueueDefinition { Name = otherQueue, Durable = true, DeadLetterExchange = _deadLetterExchanges[deadLetterQueue], DeadLetterRoutingKey = "failed" });
        await Transport!.ConfigureTopologyAsync(topology, token);
        await DeadLetterAsync(deletedQueue, deadLetterQueue, [CreateTestEnvelope()]);
        await DeadLetterAsync(otherQueue, deadLetterQueue, [CreateTestEnvelope()], expectedTotal: 2);
        await DeleteQueueAsync(deletedQueue);

        var result = await Transport.ReplayDeadLettersAsync(TransportAddress.Queue(deadLetterQueue), cancellationToken: token);

        Assert.Equal(1, result.Replayed);
        Assert.Equal(1, result.Unresolved);
        Assert.Equal(1u, await GetMessageCountAsync(otherQueue));
        Assert.Equal(1u, await WaitForMessageCountAsync(deadLetterQueue, 1));
    }

    [Fact]
    public async Task ReplayDeadLetters_WhenTheMessageFailsAgain_CanBeReplayedAgain()
    {
        var token = TestContext.Current.CancellationToken;
        var (workQueue, deadLetterQueue) = await CreateDeadLetteredQueueAsync();
        await DeadLetterAsync(workQueue, deadLetterQueue, [CreateTestEnvelope()]);
        Assert.Equal(1, (await Transport!.ReplayDeadLettersAsync(TransportAddress.Queue(deadLetterQueue), cancellationToken: token)).Replayed);
        await DeadLetterAsync(workQueue, deadLetterQueue, []);

        var second = await Transport.ReplayDeadLettersAsync(TransportAddress.Queue(deadLetterQueue), cancellationToken: token);

        Assert.Equal(1, second.Replayed);
        var received = new TaskCompletionSource<TransportEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var consumer = await Transport.SubscribeAsync(TransportAddress.Queue(workQueue), (envelope, _, _) =>
        {
            received.TrySetResult(envelope);
            return Task.CompletedTask;
        }, cancellationToken: token);
        var envelope = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Equal(2L, envelope.Headers[DeadLetterReplayHeaders.ReplayCountHeader]);
    }

    [Fact]
    public async Task ReplayDeadLetters_WhenAlreadyCancelled_ThrowsAndKeepsMessagesRetained()
    {
        var (workQueue, deadLetterQueue) = await CreateDeadLetteredQueueAsync();
        await DeadLetterAsync(workQueue, deadLetterQueue, [CreateTestEnvelope()]);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Transport!.ReplayDeadLettersAsync(
            TransportAddress.Queue(deadLetterQueue), cancellationToken: cancelled.Token));

        Assert.Equal(1u, await WaitForMessageCountAsync(deadLetterQueue, 1));
    }

    [Fact]
    public async Task ReplayDeadLetters_FromEmptyQueue_ReplaysNothing()
    {
        var token = TestContext.Current.CancellationToken;
        var (_, deadLetterQueue) = await CreateDeadLetteredQueueAsync();

        var result = await Transport!.ReplayDeadLettersAsync(TransportAddress.Queue(deadLetterQueue), cancellationToken: token);

        Assert.Equal(0, result.Replayed);
        Assert.Equal(0, result.Unresolved);
    }

    private async Task<(string WorkQueue, string DeadLetterQueue)> CreateDeadLetteredQueueAsync(bool quorum = false)
    {
        var workQueue = CreateQueueName();
        var deadLetterExchange = CreateExchangeName();
        var deadLetterQueue = CreateQueueName();
        var topology = new TransportTopology();
        topology.AddExchange(new ExchangeDefinition { Name = deadLetterExchange, Type = HeroMessaging.Abstractions.Transport.ExchangeType.Direct, Durable = true });
        topology.AddQueue(new QueueDefinition
        {
            Name = deadLetterQueue,
            Durable = true,
            Arguments = quorum ? new Dictionary<string, object> { ["x-queue-type"] = "quorum" } : null
        });
        topology.AddBinding(new BindingDefinition { SourceExchange = deadLetterExchange, Destination = deadLetterQueue, RoutingKey = "failed" });
        topology.AddQueue(new QueueDefinition
        {
            Name = workQueue,
            Durable = true,
            DeadLetterExchange = deadLetterExchange,
            DeadLetterRoutingKey = "failed",
            Arguments = quorum ? new Dictionary<string, object> { ["x-queue-type"] = "quorum" } : null
        });
        await Transport!.ConfigureTopologyAsync(topology, TestContext.Current.CancellationToken);
        _deadLetterExchanges[deadLetterQueue] = deadLetterExchange;
        return (workQueue, deadLetterQueue);
    }

    private async Task DeadLetterAsync(string workQueue, string deadLetterQueue, IReadOnlyList<TransportEnvelope> envelopes, uint? expectedTotal = null)
    {
        var token = TestContext.Current.CancellationToken;
        await using var failing = await Transport!.SubscribeAsync(
            TransportAddress.Queue(workQueue),
            (_, _, _) => throw new InvalidOperationException("Simulated poison message"),
            RejectAtOnce,
            token);
        foreach (var envelope in envelopes)
            await Transport.SendAsync(TransportAddress.Queue(workQueue), envelope, token);
        await WaitForMessageCountAsync(deadLetterQueue, expectedTotal ?? (uint)Math.Max(envelopes.Count, 1));
    }

    private async Task<uint> WaitForMessageCountAsync(string queue, uint expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        uint count;
        while ((count = await GetMessageCountAsync(queue)) != expected && DateTime.UtcNow < deadline)
            await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(expected, count);
        return count;
    }

    private async Task<uint> GetMessageCountAsync(string queue)
    {
        await using var connection = await ConnectAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        var declared = await channel.QueueDeclarePassiveAsync(queue, TestContext.Current.CancellationToken);
        return declared.MessageCount;
    }

    private async Task DeleteQueueAsync(string queue)
    {
        await using var connection = await ConnectAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        await channel.QueueDeleteAsync(queue, cancellationToken: TestContext.Current.CancellationToken);
    }

    private Task<IConnection> ConnectAsync()
    {
        var factory = new ConnectionFactory
        {
            HostName = Options!.Host,
            Port = Options.Port,
            UserName = Options.UserName!,
            Password = Options.Password!
        };
        return factory.CreateConnectionAsync(TestContext.Current.CancellationToken);
    }
}
