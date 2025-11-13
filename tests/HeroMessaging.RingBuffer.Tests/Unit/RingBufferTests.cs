using HeroMessaging.RingBuffer.EventFactories;
using HeroMessaging.RingBuffer.WaitStrategies;
using Xunit;

namespace HeroMessaging.RingBuffer.Tests.Unit;

[Trait("Category", "Unit")]
public class RingBufferTests
{
    private class TestEvent
    {
        public int Value { get; set; }
        public string? Message { get; set; }
    }

    private class TestEventFactory : IEventFactory<TestEvent>
    {
        public TestEvent Create() => new TestEvent();
    }

    [Fact]
    public void Constructor_WithPowerOf2Size_CreatesSuccessfully()
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();

        // Act
        var ringBuffer = new RingBuffer<TestEvent>(
            bufferSize: 1024,
            factory,
            ProducerType.Single,
            waitStrategy);

        // Assert
        Assert.NotNull(ringBuffer);
        Assert.Equal(1024, ringBuffer.BufferSize);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    public void Constructor_WithVariousPowerOf2Sizes_CreatesSuccessfully(int size)
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();

        // Act
        var ringBuffer = new RingBuffer<TestEvent>(
            size,
            factory,
            ProducerType.Single,
            waitStrategy);

        // Assert
        Assert.Equal(size, ringBuffer.BufferSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(100)]
    [InlineData(1000)]
    public void Constructor_WithNonPowerOf2Size_ThrowsArgumentException(int size)
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new RingBuffer<TestEvent>(
                size,
                factory,
                ProducerType.Single,
                waitStrategy));

        Assert.Contains("power of 2", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_WithNegativeSize_ThrowsArgumentException()
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new RingBuffer<TestEvent>(
                -1,
                factory,
                ProducerType.Single,
                waitStrategy));

        Assert.Contains("positive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_WithNullFactory_ThrowsArgumentNullException()
    {
        // Arrange
        var waitStrategy = new SleepingWaitStrategy();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RingBuffer<TestEvent>(
                16,
                null!,
                ProducerType.Single,
                waitStrategy));
    }

    [Fact]
    public void Constructor_PreAllocatesAllSlots()
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();
        var bufferSize = 16;

        // Act
        var ringBuffer = new RingBuffer<TestEvent>(
            bufferSize,
            factory,
            ProducerType.Single,
            waitStrategy);

        // Assert - All slots should be accessible
        for (long i = 0; i < bufferSize; i++)
        {
            var evt = ringBuffer.Get(i);
            Assert.NotNull(evt);
        }
    }

    [Fact]
    public void Next_ReturnsSequentialNumbers()
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();
        var ringBuffer = new RingBuffer<TestEvent>(
            16,
            factory,
            ProducerType.Single,
            waitStrategy);

        // Act & Assert
        Assert.Equal(0, ringBuffer.Next());
        Assert.Equal(1, ringBuffer.Next());
        Assert.Equal(2, ringBuffer.Next());
        Assert.Equal(3, ringBuffer.Next());
    }

    [Fact]
    public void Get_WithSequence_ReturnsCorrectSlot()
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();
        var ringBuffer = new RingBuffer<TestEvent>(
            16,
            factory,
            ProducerType.Single,
            waitStrategy);

        // Act
        var sequence = ringBuffer.Next();
        var evt = ringBuffer.Get(sequence);
        evt.Value = 42;

        // Assert
        Assert.Equal(42, ringBuffer.Get(sequence).Value);
    }

    [Fact]
    public void Get_WrapsAroundBufferCorrectly()
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();
        var bufferSize = 4;
        var ringBuffer = new RingBuffer<TestEvent>(
            bufferSize,
            factory,
            ProducerType.Single,
            waitStrategy);

        // Act & Assert
        // Sequence 0 and 4 should map to same slot
        var evt0 = ringBuffer.Get(0);
        var evt4 = ringBuffer.Get(4);
        Assert.Same(evt0, evt4);

        // Sequence 1 and 5 should map to same slot
        var evt1 = ringBuffer.Get(1);
        var evt5 = ringBuffer.Get(5);
        Assert.Same(evt1, evt5);
    }

    [Fact]
    public void NextBatch_ReturnsCorrectHighSequence()
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();
        var ringBuffer = new RingBuffer<TestEvent>(
            16,
            factory,
            ProducerType.Single,
            waitStrategy);

        // Act
        var highSequence = ringBuffer.Next(5);

        // Assert
        Assert.Equal(4, highSequence); // 0-4 claimed (5 total)
    }

    [Fact]
    public void PublishAndGet_SingleProducer_WorksCorrectly()
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();
        var ringBuffer = new RingBuffer<TestEvent>(
            16,
            factory,
            ProducerType.Single,
            waitStrategy);

        // Act
        var sequence = ringBuffer.Next();
        var evt = ringBuffer.Get(sequence);
        evt.Value = 99;
        evt.Message = "Test";
        ringBuffer.Publish(sequence);

        // Assert
        Assert.Equal(99, ringBuffer.Get(sequence).Value);
        Assert.Equal("Test", ringBuffer.Get(sequence).Message);
    }

    [Fact]
    public void PublishRange_WorksCorrectly()
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();
        var ringBuffer = new RingBuffer<TestEvent>(
            16,
            factory,
            ProducerType.Single,
            waitStrategy);

        // Act
        var hi = ringBuffer.Next(3);
        var lo = hi - 2;

        for (long seq = lo; seq <= hi; seq++)
        {
            var evt = ringBuffer.Get(seq);
            evt.Value = (int)seq;
        }

        ringBuffer.Publish(lo, hi);

        // Assert
        for (long seq = lo; seq <= hi; seq++)
        {
            Assert.Equal(seq, ringBuffer.Get(seq).Value);
        }
    }

    [Fact]
    public void GetCursor_ReturnsHighestPublishedSequence()
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();
        var ringBuffer = new RingBuffer<TestEvent>(
            16,
            factory,
            ProducerType.Single,
            waitStrategy);

        // Act
        Assert.Equal(-1, ringBuffer.GetCursor()); // Nothing published

        var seq0 = ringBuffer.Next();
        ringBuffer.Publish(seq0);
        Assert.Equal(0, ringBuffer.GetCursor());

        var seq1 = ringBuffer.Next();
        ringBuffer.Publish(seq1);
        Assert.Equal(1, ringBuffer.GetCursor());
    }

    [Fact]
    public void NewBarrier_CreatesSequenceBarrier()
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();
        var ringBuffer = new RingBuffer<TestEvent>(
            16,
            factory,
            ProducerType.Single,
            waitStrategy);

        // Act
        var barrier = ringBuffer.NewBarrier();

        // Assert
        Assert.NotNull(barrier);
    }

    [Fact]
    public void GetRemainingCapacity_ReturnsCorrectValue()
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();
        var bufferSize = 16;
        var ringBuffer = new RingBuffer<TestEvent>(
            bufferSize,
            factory,
            ProducerType.Single,
            waitStrategy);

        // Act & Assert
        Assert.Equal(bufferSize, ringBuffer.GetRemainingCapacity());

        var seq = ringBuffer.Next();
        ringBuffer.Publish(seq);

        // Still has capacity since no consumers
        Assert.Equal(bufferSize, ringBuffer.GetRemainingCapacity());
    }

    [Theory]
    [InlineData(ProducerType.Single)]
    [InlineData(ProducerType.Multi)]
    public void Constructor_WithDifferentProducerTypes_CreatesSuccessfully(ProducerType producerType)
    {
        // Arrange
        var factory = new TestEventFactory();
        var waitStrategy = new SleepingWaitStrategy();

        // Act
        var ringBuffer = new RingBuffer<TestEvent>(
            16,
            factory,
            producerType,
            waitStrategy);

        // Assert
        Assert.NotNull(ringBuffer);
    }
}
