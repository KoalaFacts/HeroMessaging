using HeroMessaging.RingBuffer.Sequencers;
using HeroMessaging.RingBuffer.WaitStrategies;
using Xunit;

namespace HeroMessaging.RingBuffer.Tests.Unit.Sequencers;

[Trait("Category", "Unit")]
public class SingleProducerSequencerTests
{
    [Fact]
    public void Next_StartsAtZero()
    {
        // Arrange
        var sequencer = new SingleProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        var sequence = sequencer.Next();

        // Assert
        Assert.Equal(0, sequence);
    }

    [Fact]
    public void Next_ReturnsSequentialNumbers()
    {
        // Arrange
        var sequencer = new SingleProducerSequencer(16, new YieldingWaitStrategy());

        // Act & Assert
        Assert.Equal(0, sequencer.Next());
        Assert.Equal(1, sequencer.Next());
        Assert.Equal(2, sequencer.Next());
        Assert.Equal(3, sequencer.Next());
    }

    [Fact]
    public void NextBatch_ReturnsCorrectHighSequence()
    {
        // Arrange
        var sequencer = new SingleProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        var highSequence = sequencer.Next(5);

        // Assert
        Assert.Equal(4, highSequence); // 0-4 (5 sequences)
    }

    [Fact]
    public void NextBatch_WithZero_ThrowsArgumentException()
    {
        // Arrange
        var sequencer = new SingleProducerSequencer(16, new YieldingWaitStrategy());

        // Act & Assert
        Assert.Throws<ArgumentException>(() => sequencer.Next(0));
    }

    [Fact]
    public void NextBatch_WithNegative_ThrowsArgumentException()
    {
        // Arrange
        var sequencer = new SingleProducerSequencer(16, new YieldingWaitStrategy());

        // Act & Assert
        Assert.Throws<ArgumentException>(() => sequencer.Next(-1));
    }

    [Fact]
    public void NextBatch_ExceedingBufferSize_ThrowsArgumentException()
    {
        // Arrange
        var sequencer = new SingleProducerSequencer(16, new YieldingWaitStrategy());

        // Act & Assert
        Assert.Throws<ArgumentException>(() => sequencer.Next(17));
    }

    [Fact]
    public void Publish_UpdatesCursor()
    {
        // Arrange
        var sequencer = new SingleProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        var seq = sequencer.Next();
        sequencer.Publish(seq);

        // Assert
        Assert.Equal(0, sequencer.GetCursor());
    }

    [Fact]
    public void Publish_MultipleSequences_UpdatesCursor()
    {
        // Arrange
        var sequencer = new SingleProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        for (int i = 0; i < 5; i++)
        {
            var seq = sequencer.Next();
            sequencer.Publish(seq);
        }

        // Assert
        Assert.Equal(4, sequencer.GetCursor());
    }

    [Fact]
    public void PublishRange_UpdatesCursorToHighSequence()
    {
        // Arrange
        var sequencer = new SingleProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        var hi = sequencer.Next(3);
        var lo = hi - 2;
        sequencer.Publish(lo, hi);

        // Assert
        Assert.Equal(hi, sequencer.GetCursor());
    }

    [Fact]
    public void GetCursor_InitialValue_IsNegativeOne()
    {
        // Arrange
        var sequencer = new SingleProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        var cursor = sequencer.GetCursor();

        // Assert
        Assert.Equal(-1, cursor);
    }
}
