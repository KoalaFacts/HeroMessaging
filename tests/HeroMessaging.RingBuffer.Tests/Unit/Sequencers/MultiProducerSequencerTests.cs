using HeroMessaging.RingBuffer.Sequencers;
using HeroMessaging.RingBuffer.WaitStrategies;
using Xunit;

namespace HeroMessaging.RingBuffer.Tests.Unit.Sequencers;

[Trait("Category", "Unit")]
public class MultiProducerSequencerTests
{
    [Fact]
    public void Next_StartsAtZero()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        var sequence = sequencer.Next();

        // Assert
        Assert.Equal(0, sequence);
    }

    [Fact]
    public void Next_ReturnsSequentialNumbers_SingleThreaded()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(16, new YieldingWaitStrategy());

        // Act & Assert
        Assert.Equal(0, sequencer.Next());
        Assert.Equal(1, sequencer.Next());
        Assert.Equal(2, sequencer.Next());
        Assert.Equal(3, sequencer.Next());
    }

    [Fact]
    public void Next_ConcurrentCalls_ReturnsUniqueSequences()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(1024, new YieldingWaitStrategy());
        var sequences = new long[100];
        var tasks = new Task[100];

        // Act
        for (int i = 0; i < 100; i++)
        {
            int index = i;
            tasks[i] = Task.Run(() =>
            {
                sequences[index] = sequencer.Next();
            });
        }

        Task.WaitAll(tasks);

        // Assert
        var uniqueSequences = sequences.Distinct().ToArray();
        Assert.Equal(100, uniqueSequences.Length); // All unique
    }

    [Fact]
    public void NextBatch_ReturnsCorrectHighSequence()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        var highSequence = sequencer.Next(5);

        // Assert
        Assert.Equal(4, highSequence); // 0-4 (5 sequences)
    }

    [Fact]
    public void NextBatch_WithZero_ThrowsArgumentException()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(16, new YieldingWaitStrategy());

        // Act & Assert
        Assert.Throws<ArgumentException>(() => sequencer.Next(0));
    }

    [Fact]
    public void NextBatch_WithNegative_ThrowsArgumentException()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(16, new YieldingWaitStrategy());

        // Act & Assert
        Assert.Throws<ArgumentException>(() => sequencer.Next(-1));
    }

    [Fact]
    public void NextBatch_ExceedingBufferSize_ThrowsArgumentException()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(16, new YieldingWaitStrategy());

        // Act & Assert
        Assert.Throws<ArgumentException>(() => sequencer.Next(17));
    }

    [Fact]
    public void Publish_MarksSequenceAsAvailable()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        var seq = sequencer.Next();
        sequencer.Publish(seq);

        // Assert
        Assert.True(sequencer.IsAvailable(seq));
    }

    [Fact]
    public void Publish_MultipleSequences_MarksAllAsAvailable()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        var seq0 = sequencer.Next();
        var seq1 = sequencer.Next();
        var seq2 = sequencer.Next();

        sequencer.Publish(seq0);
        sequencer.Publish(seq1);
        sequencer.Publish(seq2);

        // Assert
        Assert.True(sequencer.IsAvailable(seq0));
        Assert.True(sequencer.IsAvailable(seq1));
        Assert.True(sequencer.IsAvailable(seq2));
    }

    [Fact]
    public void PublishRange_MarksAllSequencesInRangeAsAvailable()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        var hi = sequencer.Next(3);
        var lo = hi - 2;
        sequencer.Publish(lo, hi);

        // Assert
        for (long seq = lo; seq <= hi; seq++)
        {
            Assert.True(sequencer.IsAvailable(seq));
        }
    }

    [Fact]
    public void IsAvailable_UnpublishedSequence_ReturnsFalse()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        var seq = sequencer.Next();
        // Don't publish

        // Assert
        Assert.False(sequencer.IsAvailable(seq));
    }

    [Fact]
    public void GetCursor_ReturnsHighestClaimedSequence()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        Assert.Equal(-1, sequencer.GetCursor()); // Initial

        sequencer.Next();
        Assert.Equal(0, sequencer.GetCursor());

        sequencer.Next();
        Assert.Equal(1, sequencer.GetCursor());
    }

    [Fact]
    public void GetHighestPublishedSequence_WithAllPublished_ReturnsMaximum()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        var seq0 = sequencer.Next();
        var seq1 = sequencer.Next();
        var seq2 = sequencer.Next();

        sequencer.Publish(seq0);
        sequencer.Publish(seq1);
        sequencer.Publish(seq2);

        var result = sequencer.GetHighestPublishedSequence(0, 2);

        // Assert
        Assert.Equal(2, result);
    }

    [Fact]
    public void GetHighestPublishedSequence_WithGap_ReturnsBeforeGap()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(16, new YieldingWaitStrategy());

        // Act
        var seq0 = sequencer.Next();
        var seq1 = sequencer.Next();
        var seq2 = sequencer.Next();

        sequencer.Publish(seq0);
        // seq1 not published (gap)
        sequencer.Publish(seq2);

        var result = sequencer.GetHighestPublishedSequence(0, 2);

        // Assert
        Assert.Equal(0, result); // Can only read up to seq0
    }

    [Fact]
    public void ConcurrentPublish_AllSequencesMarkedAvailable()
    {
        // Arrange
        var sequencer = new MultiProducerSequencer(1024, new YieldingWaitStrategy());
        var sequenceCount = 100;
        var sequences = new long[sequenceCount];
        var tasks = new Task[sequenceCount];

        // Act - Claim and publish concurrently
        for (int i = 0; i < sequenceCount; i++)
        {
            int index = i;
            tasks[i] = Task.Run(() =>
            {
                var seq = sequencer.Next();
                sequences[index] = seq;
                sequencer.Publish(seq);
            });
        }

        Task.WaitAll(tasks);

        // Assert - All sequences should be available
        foreach (var seq in sequences)
        {
            Assert.True(sequencer.IsAvailable(seq));
        }
    }
}
