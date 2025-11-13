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
    public void Instance_ReturnsSameInstanceOnMultipleCalls()
    {
        // Act
        var random1 = RandomHelper.Instance;
        var random2 = RandomHelper.Instance;

        // Assert
        Assert.Same(random1, random2);
    }

    [Fact]
    public void Instance_GeneratesRandomNumbers()
    {
        // Arrange
        var random = RandomHelper.Instance;

        // Act
        var number1 = random.Next(0, 1000);
        var number2 = random.Next(0, 1000);
        var number3 = random.Next(0, 1000);

        // Assert
        Assert.InRange(number1, 0, 999);
        Assert.InRange(number2, 0, 999);
        Assert.InRange(number3, 0, 999);
    }

    [Fact]
    public void Instance_NextInt_ReturnsValueInRange()
    {
        // Arrange
        var random = RandomHelper.Instance;
        var min = 10;
        var max = 20;

        // Act
        var values = new List<int>();
        for (int i = 0; i < 100; i++)
        {
            values.Add(random.Next(min, max));
        }

        // Assert
        Assert.All(values, value =>
        {
            Assert.InRange(value, min, max - 1);
        });
    }

    [Fact]
    public void Instance_NextDouble_ReturnsValueBetweenZeroAndOne()
    {
        // Arrange
        var random = RandomHelper.Instance;

        // Act
        var values = new List<double>();
        for (int i = 0; i < 100; i++)
        {
            values.Add(random.NextDouble());
        }

        // Assert
        Assert.All(values, value =>
        {
            Assert.InRange(value, 0.0, 1.0);
        });
    }

    [Fact]
    public void Instance_NextBytes_FillsArrayWithRandomData()
    {
        // Arrange
        var random = RandomHelper.Instance;
        var buffer = new byte[16];

        // Act
        random.NextBytes(buffer);

        // Assert
        Assert.NotEqual(new byte[16], buffer); // Should not be all zeros
    }

    [Fact]
    public void Instance_ThreadSafe_ProducesDistinctValuesAcrossThreads()
    {
        // Arrange
        var results = new System.Collections.Concurrent.ConcurrentBag<int>();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var random = RandomHelper.Instance;
                for (int j = 0; j < 100; j++)
                {
                    results.Add(random.Next(0, 10000));
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.Equal(1000, results.Count);
        // Should have variety in generated numbers
        var distinctCount = results.Distinct().Count();
        Assert.True(distinctCount > 900, $"Expected high variety in random numbers, got {distinctCount} distinct values");
    }

    #endregion

    #region Consistency Tests

    [Fact]
    public void Instance_MultipleAccesses_WorksCorrectly()
    {
        // Act & Assert
        for (int i = 0; i < 100; i++)
        {
            var random = RandomHelper.Instance;
            var value = random.Next();
            Assert.True(value >= 0);
        }
    }

    [Fact]
    public void Instance_UsedInParallel_DoesNotThrow()
    {
        // Arrange
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        // Act
        Parallel.For(0, 100, i =>
        {
            try
            {
                var random = RandomHelper.Instance;
                var value = random.Next(0, 1000);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        // Assert
        Assert.Empty(exceptions);
    }

    #endregion

    #region Statistical Tests

    [Fact]
    public void Instance_GeneratesUniformDistribution()
    {
        // Arrange
        var random = RandomHelper.Instance;
        var buckets = new int[10];
        var iterations = 10000;

        // Act
        for (int i = 0; i < iterations; i++)
        {
            var value = random.Next(0, 10);
            buckets[value]++;
        }

        // Assert - Each bucket should have roughly 10% of values (with tolerance)
        var expectedPerBucket = iterations / 10.0;
        var tolerance = expectedPerBucket * 0.2; // 20% tolerance

        foreach (var count in buckets)
        {
            Assert.InRange(count, expectedPerBucket - tolerance, expectedPerBucket + tolerance);
        }
    }

    [Fact]
    public void Instance_NextBytes_ProducesVariedOutput()
    {
        // Arrange
        var random = RandomHelper.Instance;
        var buffer1 = new byte[32];
        var buffer2 = new byte[32];

        // Act
        random.NextBytes(buffer1);
        random.NextBytes(buffer2);

        // Assert
        Assert.NotEqual(buffer1, buffer2);
    }

    #endregion
}
