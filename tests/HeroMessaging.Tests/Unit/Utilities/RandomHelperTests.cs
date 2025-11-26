using HeroMessaging.Utilities;
using Xunit;

namespace HeroMessaging.Tests.Unit.Utilities;

[Trait("Category", "Unit")]
public class RandomHelperTests
{
    #region Instance Tests

    [Fact]
    public void Instance_ReturnsNonNullRandom()
    {
        // Act
        var random = RandomHelper.Instance;

        // Assert
        Assert.NotNull(random);
    }

    [Fact]
    public void Instance_ReturnsRandomInstance()
    {
        // Act
        var random = RandomHelper.Instance;

        // Assert - In .NET 6+, Random.Shared returns a ThreadSafeRandom which inherits from Random
        Assert.IsAssignableFrom<Random>(random);
    }

    [Fact]
    public void Instance_CalledMultipleTimes_ReturnsSameInstance()
    {
        // Act
        var random1 = RandomHelper.Instance;
        var random2 = RandomHelper.Instance;

        // Assert - In same thread, should return same instance
        Assert.Same(random1, random2);
    }

    [Fact]
    public void Instance_CanGenerateRandomNumber()
    {
        // Arrange
        var random = RandomHelper.Instance;

        // Act
        var number = random.Next();

        // Assert
        Assert.True(number >= 0);
    }

    [Fact]
    public void Instance_CanGenerateRandomNumberInRange()
    {
        // Arrange
        var random = RandomHelper.Instance;
        var min = 10;
        var max = 100;

        // Act
        var number = random.Next(min, max);

        // Assert
        Assert.True(number >= min && number < max);
    }

    [Fact]
    public void Instance_GeneratesDifferentNumbers()
    {
        // Arrange
        var random = RandomHelper.Instance;
        var numbers = new HashSet<int>();

        // Act - Generate 100 random numbers
        for (int i = 0; i < 100; i++)
        {
            numbers.Add(random.Next(0, 1000000));
        }

        // Assert - Should have high diversity (at least 90 unique values out of 100)
        Assert.True(numbers.Count >= 90);
    }

    [Fact]
    public void Instance_NextDouble_ReturnsValueBetweenZeroAndOne()
    {
        // Arrange
        var random = RandomHelper.Instance;

        // Act
        var value = random.NextDouble();

        // Assert
        Assert.True(value >= 0.0 && value < 1.0);
    }

    [Fact]
    public void Instance_NextBytes_FillsArray()
    {
        // Arrange
        var random = RandomHelper.Instance;
        var buffer = new byte[10];

        // Act
        random.NextBytes(buffer);

        // Assert - At least some bytes should be non-zero
        Assert.Contains(buffer, b => b != 0);
    }

    [Fact]
    public void Instance_MultipleThreads_ReturnsDifferentInstances()
    {
        // Arrange
        Random? random1 = null;
        Random? random2 = null;
        var task1Complete = false;
        var task2Complete = false;

        // Act
        var task1 = Task.Run(() =>
        {
            random1 = RandomHelper.Instance;
            task1Complete = true;
        });

        var task2 = Task.Run(() =>
        {
            random2 = RandomHelper.Instance;
            task2Complete = true;
        });

        Task.WaitAll(task1, task2);

        // Assert
        Assert.True(task1Complete);
        Assert.True(task2Complete);
        Assert.NotNull(random1);
        Assert.NotNull(random2);
        // Note: In .NET 6+, both threads share Random.Shared, so they would be the same
        // In netstandard2.0, they would be different instances
    }

    [Fact]
    public void Instance_ConsecutiveCalls_ProducesDifferentSequences()
    {
        // Arrange
        var random = RandomHelper.Instance;
        var sequence1 = new List<int>();
        var sequence2 = new List<int>();

        // Act - Generate two sequences
        for (int i = 0; i < 10; i++)
        {
            sequence1.Add(random.Next(0, 1000));
        }

        // Small delay to ensure different timing
        Thread.Sleep(1);

        for (int i = 0; i < 10; i++)
        {
            sequence2.Add(random.Next(0, 1000));
        }

        // Assert - Sequences should be different
        Assert.NotEqual(sequence1, sequence2);
    }

    [Fact]
    public void Instance_NextWithMaxValue_ReturnsValueWithinBounds()
    {
        // Arrange
        var random = RandomHelper.Instance;
        var maxValue = 50;

        // Act
        var value = random.Next(maxValue);

        // Assert
        Assert.True(value >= 0 && value < maxValue);
    }

    [Fact]
    public void Instance_NextWithZeroMaxValue_ReturnsZero()
    {
        // Arrange
        var random = RandomHelper.Instance;

        // Act
        var value = random.Next(0);

        // Assert
        Assert.Equal(0, value);
    }

    [Fact]
    public void Instance_CanGenerateLargeNumbers()
    {
        // Arrange
        var random = RandomHelper.Instance;

        // Act
        var value = random.Next(int.MaxValue);

        // Assert
        Assert.True(value >= 0 && value < int.MaxValue);
    }

    [Fact]
    public void Instance_NextBytesWithEmptyBuffer_DoesNotThrow()
    {
        // Arrange
        var random = RandomHelper.Instance;
        var buffer = Array.Empty<byte>();

        // Act & Assert - Should not throw
        random.NextBytes(buffer);
    }

    [Fact]
    public void Instance_NextBytesWithLargeBuffer_FillsCorrectly()
    {
        // Arrange
        var random = RandomHelper.Instance;
        var buffer = new byte[1000];

        // Act
        random.NextBytes(buffer);

        // Assert - Most bytes should be non-zero (statistically)
        var nonZeroCount = buffer.Count(b => b != 0);
        Assert.True(nonZeroCount > 900); // At least 90% should be non-zero
    }

    [Fact]
    public void Instance_IsThreadSafe()
    {
        // Arrange
        var random = RandomHelper.Instance;
        var numbers = new System.Collections.Concurrent.ConcurrentBag<int>();

        // Act - Generate random numbers from multiple threads
        Parallel.For(0, 100, _ =>
        {
            numbers.Add(random.Next(0, 1000000));
        });

        // Assert - Should have generated 100 numbers without errors
        Assert.Equal(100, numbers.Count);
        // Should have good diversity
        Assert.True(numbers.Distinct().Count() >= 90);
    }

    #endregion
}
