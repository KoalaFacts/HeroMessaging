using HeroMessaging.RingBuffer.EventFactories;
using HeroMessaging.RingBuffer.EventHandlers;
using HeroMessaging.RingBuffer.EventProcessors;
using HeroMessaging.RingBuffer.WaitStrategies;
using Xunit;

namespace HeroMessaging.RingBuffer.Tests.Unit.EventProcessors;

[Trait("Category", "Unit")]
public class BatchEventProcessorTests
{
    private class TestEvent
    {
        public int Value { get; set; }
    }

    private class TestEventFactory : IEventFactory<TestEvent>
    {
        public TestEvent Create() => new TestEvent();
    }

    private class TestEventHandler : IEventHandler<TestEvent>
    {
        private readonly List<(TestEvent data, long sequence, bool endOfBatch)> _events = new();
        private readonly List<Exception> _errors = new();
        private bool _shutdownCalled;

        public IReadOnlyList<(TestEvent data, long sequence, bool endOfBatch)> Events => _events;
        public IReadOnlyList<Exception> Errors => _errors;
        public bool ShutdownCalled => _shutdownCalled;

        public void OnEvent(TestEvent data, long sequence, bool endOfBatch)
        {
            _events.Add((data, sequence, endOfBatch));
        }

        public void OnError(Exception ex)
        {
            _errors.Add(ex);
        }

        public void OnShutdown()
        {
            _shutdownCalled = true;
        }
    }

    [Fact]
    public void Constructor_WithValidArguments_CreatesSuccessfully()
    {
        // Arrange
        var ringBuffer = CreateRingBuffer();
        var barrier = ringBuffer.NewBarrier();
        var handler = new TestEventHandler();

        // Act
        var processor = new BatchEventProcessor<TestEvent>(ringBuffer, barrier, handler);

        // Assert
        Assert.NotNull(processor);
        Assert.NotNull(processor.Sequence);
        Assert.Equal(-1, processor.Sequence.Value);
        Assert.False(processor.IsRunning);
    }

    [Fact]
    public void Constructor_WithNullRingBuffer_ThrowsArgumentNullException()
    {
        // Arrange
        var ringBuffer = CreateRingBuffer();
        var barrier = ringBuffer.NewBarrier();
        var handler = new TestEventHandler();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new BatchEventProcessor<TestEvent>(null!, barrier, handler));
    }

    [Fact]
    public void Constructor_WithNullBarrier_ThrowsArgumentNullException()
    {
        // Arrange
        var ringBuffer = CreateRingBuffer();
        var handler = new TestEventHandler();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new BatchEventProcessor<TestEvent>(ringBuffer, null!, handler));
    }

    [Fact]
    public void Constructor_WithNullHandler_ThrowsArgumentNullException()
    {
        // Arrange
        var ringBuffer = CreateRingBuffer();
        var barrier = ringBuffer.NewBarrier();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new BatchEventProcessor<TestEvent>(ringBuffer, barrier, null!));
    }

    [Fact]
    public void Start_SetsIsRunningToTrue()
    {
        // Arrange
        var ringBuffer = CreateRingBuffer();
        var barrier = ringBuffer.NewBarrier();
        var handler = new TestEventHandler();
        var processor = new BatchEventProcessor<TestEvent>(ringBuffer, barrier, handler);

        // Act
        processor.Start();
        Thread.Sleep(10); // Give it time to start

        // Assert
        Assert.True(processor.IsRunning);

        // Cleanup
        processor.Stop();
        processor.Dispose();
    }

    [Fact]
    public void Start_CalledTwice_DoesNotStartTwice()
    {
        // Arrange
        var ringBuffer = CreateRingBuffer();
        var barrier = ringBuffer.NewBarrier();
        var handler = new TestEventHandler();
        var processor = new BatchEventProcessor<TestEvent>(ringBuffer, barrier, handler);

        // Act
        processor.Start();
        var firstState = processor.IsRunning;
        processor.Start(); // Should be no-op

        // Assert
        Assert.True(firstState);
        Assert.True(processor.IsRunning);

        // Cleanup
        processor.Stop();
        processor.Dispose();
    }

    [Fact]
    public void Stop_SetsIsRunningToFalse()
    {
        // Arrange
        var ringBuffer = CreateRingBuffer();
        var barrier = ringBuffer.NewBarrier();
        var handler = new TestEventHandler();
        var processor = new BatchEventProcessor<TestEvent>(ringBuffer, barrier, handler);

        // Act
        processor.Start();
        Thread.Sleep(10);
        processor.Stop();
        Thread.Sleep(100); // Give it time to stop

        // Assert
        Assert.False(processor.IsRunning);

        // Cleanup
        processor.Dispose();
    }

    [Fact]
    public void ProcessEvents_SingleEvent_ProcessedSuccessfully()
    {
        // Arrange
        var ringBuffer = CreateRingBuffer();
        var barrier = ringBuffer.NewBarrier();
        var handler = new TestEventHandler();
        var processor = new BatchEventProcessor<TestEvent>(ringBuffer, barrier, handler);

        ringBuffer.AddGatingSequence(processor.Sequence);

        // Act
        processor.Start();
        Thread.Sleep(10);

        var seq = ringBuffer.Next();
        var evt = ringBuffer.Get(seq);
        evt.Value = 42;
        ringBuffer.Publish(seq);

        Thread.Sleep(100); // Wait for processing

        processor.Stop();
        processor.Dispose();

        // Assert
        Assert.Single(handler.Events);
        Assert.Equal(42, handler.Events[0].data.Value);
        Assert.Equal(0, handler.Events[0].sequence);
        Assert.True(handler.Events[0].endOfBatch);
    }

    [Fact]
    public void ProcessEvents_MultipleEvents_ProcessedInOrder()
    {
        // Arrange
        var ringBuffer = CreateRingBuffer();
        var barrier = ringBuffer.NewBarrier();
        var handler = new TestEventHandler();
        var processor = new BatchEventProcessor<TestEvent>(ringBuffer, barrier, handler);

        ringBuffer.AddGatingSequence(processor.Sequence);

        // Act
        processor.Start();
        Thread.Sleep(10);

        for (int i = 0; i < 5; i++)
        {
            var seq = ringBuffer.Next();
            var evt = ringBuffer.Get(seq);
            evt.Value = i;
            ringBuffer.Publish(seq);
        }

        Thread.Sleep(100); // Wait for processing

        processor.Stop();
        processor.Dispose();

        // Assert
        Assert.Equal(5, handler.Events.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i, handler.Events[i].data.Value);
            Assert.Equal(i, handler.Events[i].sequence);
        }
    }

    [Fact]
    public void ProcessEvents_BatchOfEvents_LastMarkedAsEndOfBatch()
    {
        // Arrange
        var ringBuffer = CreateRingBuffer();
        var barrier = ringBuffer.NewBarrier();
        var handler = new TestEventHandler();
        var processor = new BatchEventProcessor<TestEvent>(ringBuffer, barrier, handler);

        ringBuffer.AddGatingSequence(processor.Sequence);

        // Act
        processor.Start();
        Thread.Sleep(10);

        // Publish batch
        var hi = ringBuffer.Next(3);
        for (long seq = hi - 2; seq <= hi; seq++)
        {
            var evt = ringBuffer.Get(seq);
            evt.Value = (int)seq;
        }
        ringBuffer.Publish(hi - 2, hi);

        Thread.Sleep(100); // Wait for processing

        processor.Stop();
        processor.Dispose();

        // Assert
        Assert.Equal(3, handler.Events.Count);
        Assert.False(handler.Events[0].endOfBatch);
        Assert.False(handler.Events[1].endOfBatch);
        Assert.True(handler.Events[2].endOfBatch); // Last one
    }

    [Fact]
    public void Dispose_CallsOnShutdown()
    {
        // Arrange
        var ringBuffer = CreateRingBuffer();
        var barrier = ringBuffer.NewBarrier();
        var handler = new TestEventHandler();
        var processor = new BatchEventProcessor<TestEvent>(ringBuffer, barrier, handler);

        processor.Start();
        Thread.Sleep(10);

        // Act
        processor.Dispose();
        Thread.Sleep(50);

        // Assert
        Assert.True(handler.ShutdownCalled);
    }

    [Fact]
    public void Sequence_UpdatesAsEventsProcessed()
    {
        // Arrange
        var ringBuffer = CreateRingBuffer();
        var barrier = ringBuffer.NewBarrier();
        var handler = new TestEventHandler();
        var processor = new BatchEventProcessor<TestEvent>(ringBuffer, barrier, handler);

        ringBuffer.AddGatingSequence(processor.Sequence);

        // Act
        processor.Start();
        Thread.Sleep(10);

        Assert.Equal(-1, processor.Sequence.Value); // Initial

        for (int i = 0; i < 5; i++)
        {
            var seq = ringBuffer.Next();
            ringBuffer.Get(seq).Value = i;
            ringBuffer.Publish(seq);
        }

        Thread.Sleep(100); // Wait for processing

        processor.Stop();
        processor.Dispose();

        // Assert
        Assert.Equal(4, processor.Sequence.Value); // Should be updated to last processed
    }

    private static RingBuffer<TestEvent> CreateRingBuffer(int size = 16)
    {
        return new RingBuffer<TestEvent>(
            size,
            new TestEventFactory(),
            ProducerType.Single,
            new YieldingWaitStrategy());
    }
}
