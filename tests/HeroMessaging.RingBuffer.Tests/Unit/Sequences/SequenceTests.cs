using HeroMessaging.RingBuffer.Sequences;
using Xunit;

namespace HeroMessaging.RingBuffer.Tests.Unit.Sequences;

[Trait("Category", "Unit")]
public class SequenceTests
{
    [Fact]
    public void Constructor_DefaultValue_SetsToNegativeOne()
    {
        // Arrange & Act
        var sequence = new Sequence(-1);

        // Assert
        Assert.Equal(-1, sequence.Value);
    }

    [Fact]
    public void Constructor_WithInitialValue_SetsValue()
    {
        // Arrange
        const long initialValue = 42;

        // Act
        var sequence = new Sequence(initialValue);

        // Assert
        Assert.Equal(initialValue, sequence.Value);
    }

    [Fact]
    public void Value_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var sequence = new Sequence(-1);
        const long expectedValue = 100;

        // Act
        sequence.Value = expectedValue;

        // Assert
        Assert.Equal(expectedValue, sequence.Value);
    }

    [Fact]
    public void Value_MultipleUpdates_ReturnsLatestValue()
    {
        // Arrange
        var sequence = new Sequence(0)
        {
            Value = 10
        };
        sequence.Value = 20;
        sequence.Value = 30;

        // Assert
        Assert.Equal(30, sequence.Value);
    }

    [Fact]
    public void Value_NegativeValues_HandledCorrectly()
    {
        // Arrange
        var sequence = new Sequence(-1)
        {
            Value = -100
        };

        // Assert
        Assert.Equal(-100, sequence.Value);
    }

    [Fact]
    public void Value_MaxValue_HandledCorrectly()
    {
        // Arrange
        var sequence = new Sequence(-1)
        {
            Value = long.MaxValue
        };

        // Assert
        Assert.Equal(long.MaxValue, sequence.Value);
    }

    [Fact]
    public void Value_MinValue_HandledCorrectly()
    {
        // Arrange
        var sequence = new Sequence(-1)
        {
            Value = long.MinValue
        };

        // Assert
        Assert.Equal(long.MinValue, sequence.Value);
    }

    [Fact]
    public void ToString_ReturnsStringRepresentationOfValue()
    {
        // Arrange
        var sequence = new Sequence(42);

        // Act
        var result = sequence.ToString();

        // Assert
        Assert.Equal("42", result);
    }

    [Fact]
    public async Task Value_ConcurrentReads_ReturnConsistentValue()
    {
        // Arrange
        var sequence = new Sequence(100);
        var results = new long[10];
        var tasks = new Task[10];

        // Act
        for (int i = 0; i < 10; i++)
        {
            int index = i;
            tasks[i] = Task.Run(() =>
            {
                results[index] = sequence.Value;
            }, TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, value => Assert.Equal(100, value));
    }

    [Fact]
    public async Task Value_ConcurrentWrites_LastWriteWins()
    {
        // Arrange
        var sequence = new Sequence(0);
        var tasks = new Task[100];

        // Act
        for (int i = 0; i < 100; i++)
        {
            int value = i;
            tasks[i] = Task.Run(() =>
            {
                sequence.Value = value;
            }, TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(tasks);

        // Assert
        // One of the values 0-99 should be the final value
        Assert.InRange(sequence.Value, 0, 99);
    }
}
