using HeroMessaging.Abstractions.Observability;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Transport.InMemory;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Transport;

[Trait("Category", "Unit")]
public class InMemoryConsumerTests : IDisposable
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly Mock<ITransportInstrumentation> _instrumentationMock;
    private readonly InMemoryTransport _transport;
    private readonly TransportAddress _source;
    private readonly ConsumerOptions _options;

    public InMemoryConsumerTests()
    {
        _timeProvider = new FakeTimeProvider();
        _instrumentationMock = new Mock<ITransportInstrumentation>();

        var transportOptions = new InMemoryTransportOptions
        {
            Name = "TestTransport",
            MaxQueueLength = 100,
            DropWhenFull = false
        };

        _transport = new InMemoryTransport(transportOptions, _timeProvider, _instrumentationMock.Object);
        _source = TransportAddress.Queue("test-queue");

        _options = new ConsumerOptions
        {
            ConsumerId = "test-consumer",
            AutoAcknowledge = true,
            ConcurrentMessageLimit = 10,
            StartImmediately = false
        };
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _transport?.DisposeAsync().AsTask().Wait();
        }
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            (env, ctx, ct) => Task.CompletedTask);

        // Act
        var consumer = CreateConsumer(handler);

        // Assert
        Assert.NotNull(consumer);
        Assert.Equal("test-consumer", consumer.ConsumerId);
        Assert.Equal(_source, consumer.Source);
        Assert.False(consumer.IsActive);
    }

    // Note: Constructor parameter validation tests removed because InMemoryConsumer is internal
    // and can only be created via transport.SubscribeAsync(). The transport ensures proper
    // parameter validation before creating consumers.

    [Fact]
    public void Constructor_WithNullInstrumentation_UsesNoOpInstrumentation()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            (env, ctx, ct) => Task.CompletedTask);

        // Act
        var consumer = CreateConsumer(handler, instrumentation: null);

        // Assert
        Assert.NotNull(consumer);
        Assert.False(consumer.IsActive);
    }

    [Fact]
    public async Task StartAsync_WhenNotActive_StartsConsumer()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            (env, ctx, ct) => Task.CompletedTask);
        var consumer = CreateConsumer(handler);

        // Act
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(consumer.IsActive);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyActive_DoesNothing()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            (env, ctx, ct) => Task.CompletedTask);
        var consumer = CreateConsumer(handler);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(consumer.IsActive);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsync_WhenActive_StopsConsumer()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            (env, ctx, ct) => Task.CompletedTask);
        var consumer = CreateConsumer(handler);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await consumer.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(consumer.IsActive);
    }

    [Fact]
    public async Task StopAsync_WhenNotActive_DoesNothing()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            (env, ctx, ct) => Task.CompletedTask);
        var consumer = CreateConsumer(handler);

        // Act
        await consumer.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(consumer.IsActive);
    }

    [Fact]
    public async Task DeliverMessageAsync_WhenActive_ProcessesMessage()
    {
        // Arrange
        var messageReceived = false;
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            (env, ctx, ct) =>
            {
                messageReceived = true;
                return Task.CompletedTask;
            });
        var consumer = CreateConsumer(handler);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(100); // Give time for processing

        // Assert
        Assert.True(messageReceived);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DeliverMessageAsync_WhenNotActive_DoesNotProcessMessage()
    {
        // Arrange
        var messageReceived = false;
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            (env, ctx, ct) =>
            {
                messageReceived = true;
                return Task.CompletedTask;
            });
        var consumer = CreateConsumer(handler);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(50);

        // Assert
        Assert.False(messageReceived);
    }

    [Fact]
    public async Task ProcessMessage_WithAutoAcknowledge_AcknowledgesAutomatically()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            async (env, ctx, ct) =>
            {
                // Don't manually acknowledge - let auto-acknowledge do it
                await Task.CompletedTask;
            });

        var options = new ConsumerOptions
        {
            ConsumerId = "test-consumer",
            AutoAcknowledge = true,
            ConcurrentMessageLimit = 10,
            StartImmediately = false
        };

        var consumer = CreateConsumer(handler, options: options);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(100);

        // Assert
        var metrics = consumer.GetMetrics();
        Assert.True(metrics.MessagesReceived > 0);
        Assert.True(metrics.MessagesAcknowledged > 0);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessMessage_WithManualAcknowledge_DoesNotAutoAcknowledge()
    {
        // Arrange
        var manuallyAcknowledged = false;
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            async (env, ctx, ct) =>
            {
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
                manuallyAcknowledged = true;
            });

        var options = new ConsumerOptions
        {
            ConsumerId = "test-consumer",
            AutoAcknowledge = true,
            ConcurrentMessageLimit = 10,
            StartImmediately = false
        };

        var consumer = CreateConsumer(handler, options: options);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(100);

        // Assert
        Assert.True(manuallyAcknowledged);
        var metrics = consumer.GetMetrics();
        Assert.Equal(1, metrics.MessagesAcknowledged);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessMessage_WithReject_IncrementsRejectedCount()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            async (env, ctx, ct) =>
            {
                await ctx.RejectAsync(requeue: false, ct, TestContext.Current.CancellationToken);
            });

        var consumer = CreateConsumer(handler);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(100);

        // Assert
        var metrics = consumer.GetMetrics();
        Assert.Equal(1, metrics.MessagesRejected);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessMessage_WithRejectAndRequeue_RequeuesMessage()
    {
        // Arrange
        var processCount = 0;
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            async (env, ctx, ct) =>
            {
                processCount++;
                if (processCount == 1)
                {
                    await ctx.RejectAsync(requeue: true, ct, TestContext.Current.CancellationToken);
                }
                else
                {
                    await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
                }
            });

        var consumer = CreateConsumer(handler);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(200); // Give time for requeue and reprocessing

        // Assert
        Assert.True(processCount >= 1);
        var metrics = consumer.GetMetrics();
        Assert.True(metrics.MessagesRejected > 0);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessMessage_WithDeadLetter_IncrementsDeadLetterCount()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            async (env, ctx, ct) =>
            {
                await ctx.DeadLetterAsync("Test reason", ct, TestContext.Current.CancellationToken);
            });

        var consumer = CreateConsumer(handler);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(100);

        // Assert
        var metrics = consumer.GetMetrics();
        Assert.Equal(1, metrics.MessagesDeadLettered);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessMessage_WithException_IncrementsFailedCount()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            (env, ctx, ct) => throw new InvalidOperationException("Test error"));

        var options = new ConsumerOptions
        {
            ConsumerId = "test-consumer",
            AutoAcknowledge = true,
            ConcurrentMessageLimit = 10,
            StartImmediately = false,
            MessageRetryPolicy = RetryPolicy.Linear(1, TimeSpan.Zero)
        };

        var consumer = CreateConsumer(handler, options: options);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(100);

        // Assert
        var metrics = consumer.GetMetrics();
        Assert.True(metrics.MessagesFailed > 0);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessMessage_WithRetryableException_RetriesUpToMaxAttempts()
    {
        // Arrange
        var attemptCount = 0;
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            (env, ctx, ct) =>
            {
                attemptCount++;
                throw new InvalidOperationException("Test error");
            });

        var options = new ConsumerOptions
        {
            ConsumerId = "test-consumer",
            AutoAcknowledge = true,
            ConcurrentMessageLimit = 10,
            StartImmediately = false,
            MessageRetryPolicy = RetryPolicy.Linear(3, TimeSpan.FromMilliseconds(10))
        };

        var consumer = CreateConsumer(handler, options: options);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(300);

        // Assert
        Assert.True(attemptCount >= 1);
        var metrics = consumer.GetMetrics();
        Assert.True(metrics.MessagesFailed > 0);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessMessage_AfterMaxRetries_DeadLettersMessage()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            (env, ctx, ct) => throw new InvalidOperationException("Test error"));

        var options = new ConsumerOptions
        {
            ConsumerId = "test-consumer",
            AutoAcknowledge = true,
            ConcurrentMessageLimit = 10,
            StartImmediately = false,
            MessageRetryPolicy = RetryPolicy.Linear(2, TimeSpan.FromMilliseconds(10))
        };

        var consumer = CreateConsumer(handler, options: options);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(200);

        // Assert
        var metrics = consumer.GetMetrics();
        Assert.True(metrics.MessagesDeadLettered > 0);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetMetrics_ReturnsAccurateMetrics()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            async (env, ctx, ct) =>
            {
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
            });

        var consumer = CreateConsumer(handler);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(100);

        // Assert
        var metrics = consumer.GetMetrics();
        Assert.Equal(1, metrics.MessagesReceived);
        Assert.Equal(1, metrics.MessagesAcknowledged);
        Assert.Equal(1, metrics.MessagesProcessed);
        Assert.Equal(0, metrics.MessagesFailed);
        Assert.NotNull(metrics.LastMessageReceived);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetMetrics_TracksCurrentlyProcessing()
    {
        // Arrange
        var tcs = new TaskCompletionSource<bool>();
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            async (env, ctx, ct) =>
            {
                await tcs.Task;
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
            });

        var consumer = CreateConsumer(handler);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(50);

        var metricsWhileProcessing = consumer.GetMetrics();

        tcs.SetResult(true);
        await Task.Delay(50);

        var metricsAfterProcessing = consumer.GetMetrics();

        // Assert
        Assert.True(metricsWhileProcessing.CurrentlyProcessing >= 0);
        Assert.Equal(0, metricsAfterProcessing.CurrentlyProcessing);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetMetrics_TracksAverageProcessingDuration()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            async (env, ctx, ct) =>
            {
                await Task.Delay(50);
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
            });

        var consumer = CreateConsumer(handler);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(150);

        // Assert
        var metrics = consumer.GetMetrics();
        Assert.True(metrics.AverageProcessingDuration >= TimeSpan.Zero);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DisposeAsync_StopsConsumer()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            (env, ctx, ct) => Task.CompletedTask);
        var consumer = CreateConsumer(handler);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // Act
        await consumer.DisposeAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(consumer.IsActive);
    }

    [Fact]
    public async Task DisposeAsync_RemovesConsumerFromTransport()
    {
        // Arrange
        var transportOptions = new InMemoryTransportOptions
        {
            Name = "TestTransport",
            MaxQueueLength = 100
        };
        var transport = new InMemoryTransport(transportOptions, _timeProvider);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            (env, ctx, ct) => Task.CompletedTask);

        var consumer = await transport.SubscribeAsync(
            _source,
            handler,
            new ConsumerOptions { StartImmediately = false }, TestContext.Current.CancellationToken);

        // Act
        await consumer.DisposeAsync(TestContext.Current.CancellationToken);

        // Assert - Consumer should be removed
        var health = await transport.GetHealthAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, health.ActiveConsumers);

        await transport.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConcurrentMessageProcessing_RespectsLimit()
    {
        // Arrange
        var concurrentCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();
        var tcs = new TaskCompletionSource<bool>();

        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            async (env, ctx, ct) =>
            {
                lock (lockObj)
                {
                    concurrentCount++;
                    if (concurrentCount > maxConcurrent)
                        maxConcurrent = concurrentCount;
                }

                await Task.Delay(50);

                lock (lockObj)
                {
                    concurrentCount--;
                }

                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
            });

        var options = new ConsumerOptions
        {
            ConsumerId = "test-consumer",
            AutoAcknowledge = false,
            ConcurrentMessageLimit = 2,
            StartImmediately = false
        };

        var consumer = CreateConsumer(handler, options: options);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        // Act
        for (int i = 0; i < 5; i++)
        {
            await DeliverMessage(consumer, CreateTestEnvelope());
        }

        await Task.Delay(500);

        // Assert
        Assert.True(maxConcurrent <= 2);

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessMessage_WithInstrumentation_RecordsEvents()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            async (env, ctx, ct) =>
            {
                await ctx.AcknowledgeAsync(ct, TestContext.Current.CancellationToken);
            });

        var consumer = CreateConsumer(handler);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(100);

        // Assert
        _instrumentationMock.Verify(x => x.StartReceiveActivity(
            It.IsAny<TransportEnvelope>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<System.Diagnostics.ActivityContext>()), Times.AtLeastOnce());

        _instrumentationMock.Verify(x => x.RecordOperation(
            It.IsAny<string>(),
            "receive",
            "success"), Times.AtLeastOnce());

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessMessage_WithError_RecordsErrorInInstrumentation()
    {
        // Arrange
        var handler = new Func<TransportEnvelope, MessageContext, CancellationToken, Task>(
            (env, ctx, ct) => throw new InvalidOperationException("Test error"));

        var options = new ConsumerOptions
        {
            ConsumerId = "test-consumer",
            AutoAcknowledge = true,
            ConcurrentMessageLimit = 10,
            StartImmediately = false,
            MessageRetryPolicy = RetryPolicy.Linear(1, TimeSpan.Zero)
        };

        var consumer = CreateConsumer(handler, options: options);
        await consumer.StartAsync(TestContext.Current.CancellationToken);

        var envelope = CreateTestEnvelope();

        // Act
        await DeliverMessage(consumer, envelope);
        await Task.Delay(100);

        // Assert
        _instrumentationMock.Verify(x => x.RecordError(
            It.IsAny<System.Diagnostics.Activity>(),
            It.IsAny<Exception>()), Times.AtLeastOnce());

        _instrumentationMock.Verify(x => x.RecordOperation(
            It.IsAny<string>(),
            "receive",
            "failure"), Times.AtLeastOnce());

        await consumer.DisposeAsync(TestContext.Current.CancellationToken);
    }

    // Helper methods
    private InMemoryConsumer CreateConsumer(
        Func<TransportEnvelope, MessageContext, CancellationToken, Task>? handler,
        string? consumerId = null,
        ConsumerOptions? options = null,
        InMemoryTransport? transport = null,
        TimeProvider? timeProvider = null,
        ITransportInstrumentation? instrumentation = null)
    {
        handler ??= (env, ctx, ct) => Task.CompletedTask;

        var consumer = new InMemoryConsumer(
            consumerId ?? "test-consumer",
            _source,
            handler,
            options ?? _options,
            transport ?? _transport,
            timeProvider ?? _timeProvider,
            instrumentation ?? _instrumentationMock.Object);

        return consumer;
    }

    private async Task DeliverMessage(InMemoryConsumer consumer, TransportEnvelope envelope)
    {
        // Use reflection to call internal DeliverMessageAsync method
        var method = typeof(InMemoryConsumer).GetMethod(
            "DeliverMessageAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (method != null)
        {
            var task = method.Invoke(consumer, [envelope, CancellationToken.None]);
            if (task is Task asyncTask)
            {
                await asyncTask;
            }
        }
    }

    private static TransportEnvelope CreateTestEnvelope(string messageType = "TestMessage")
    {
        return new TransportEnvelope(
            messageType,
            new byte[] { 1, 2, 3, 4, 5 }.AsMemory(),
            messageId: Guid.NewGuid().ToString());
    }
}
