using HeroMessaging.Abstractions.Processing;

namespace HeroMessaging.Abstractions.Tests.Processing;

[Trait("Category", "Unit")]
public class BatchProcessingResultTests
{
    [Fact]
    public void Constructor_WithResults_SetsProperties()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful(),
            ProcessingResult.Failed(new Exception("Error"))
        }.AsReadOnly();

        // Act
        var batchResult = new BatchProcessingResult { Results = results };

        // Assert
        Assert.Equal(2, batchResult.TotalCount);
        Assert.Same(results, batchResult.Results);
    }

    [Fact]
    public void TotalCount_ReturnsCorrectCount()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful(),
            ProcessingResult.Successful(),
            ProcessingResult.Failed(new Exception())
        }.AsReadOnly();

        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var count = batchResult.TotalCount;

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public void SuccessCount_WithAllSuccessful_ReturnsTotal()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful(),
            ProcessingResult.Successful(),
            ProcessingResult.Successful()
        }.AsReadOnly();

        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var count = batchResult.SuccessCount;

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public void SuccessCount_WithSomeSuccessful_ReturnsCorrectCount()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful(),
            ProcessingResult.Failed(new Exception()),
            ProcessingResult.Successful()
        }.AsReadOnly();

        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var count = batchResult.SuccessCount;

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public void SuccessCount_WithAllFailed_ReturnsZero()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Failed(new Exception()),
            ProcessingResult.Failed(new Exception())
        }.AsReadOnly();

        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var count = batchResult.SuccessCount;

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public void FailureCount_CalculatesCorrectly()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful(),
            ProcessingResult.Failed(new Exception()),
            ProcessingResult.Failed(new Exception())
        }.AsReadOnly();

        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var count = batchResult.FailureCount;

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public void AllSucceeded_WithAllSuccessful_ReturnsTrue()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful(),
            ProcessingResult.Successful()
        }.AsReadOnly();

        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var allSucceeded = batchResult.AllSucceeded;

        // Assert
        Assert.True(allSucceeded);
    }

    [Fact]
    public void AllSucceeded_WithSomeFailed_ReturnsFalse()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful(),
            ProcessingResult.Failed(new Exception())
        }.AsReadOnly();

        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var allSucceeded = batchResult.AllSucceeded;

        // Assert
        Assert.False(allSucceeded);
    }

    [Fact]
    public void AnySucceeded_WithSomeSuccessful_ReturnsTrue()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful(),
            ProcessingResult.Failed(new Exception())
        }.AsReadOnly();

        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var anySucceeded = batchResult.AnySucceeded;

        // Assert
        Assert.True(anySucceeded);
    }

    [Fact]
    public void AnySucceeded_WithAllFailed_ReturnsFalse()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Failed(new Exception()),
            ProcessingResult.Failed(new Exception())
        }.AsReadOnly();

        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var anySucceeded = batchResult.AnySucceeded;

        // Assert
        Assert.False(anySucceeded);
    }

    [Fact]
    public void AllFailed_WithAllFailed_ReturnsTrue()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Failed(new Exception()),
            ProcessingResult.Failed(new Exception())
        }.AsReadOnly();

        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var allFailed = batchResult.AllFailed;

        // Assert
        Assert.True(allFailed);
    }

    [Fact]
    public void AllFailed_WithSomeSuccessful_ReturnsFalse()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful(),
            ProcessingResult.Failed(new Exception())
        }.AsReadOnly();

        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var allFailed = batchResult.AllFailed;

        // Assert
        Assert.False(allFailed);
    }

    [Fact]
    public void Message_CanBeSet()
    {
        // Arrange
        var results = new List<ProcessingResult> { ProcessingResult.Successful() }.AsReadOnly();

        // Act
        var batchResult = new BatchProcessingResult
        {
            Results = results,
            Message = "Batch processed"
        };

        // Assert
        Assert.Equal("Batch processed", batchResult.Message);
    }

    [Fact]
    public void Data_CanBeSet()
    {
        // Arrange
        var results = new List<ProcessingResult> { ProcessingResult.Successful() }.AsReadOnly();
        var data = new { ProcessedBy = "BatchProcessor", Time = DateTime.UtcNow };

        // Act
        var batchResult = new BatchProcessingResult
        {
            Results = results,
            Data = data
        };

        // Assert
        Assert.Same(data, batchResult.Data);
    }

    [Fact]
    public void Create_FactoryMethod_CreatesResult()
    {
        // Arrange
        var results = new List<ProcessingResult> { ProcessingResult.Successful() }.AsReadOnly();

        // Act
        var batchResult = BatchProcessingResult.Create(results, "Test message", "Test data");

        // Assert
        Assert.Same(results, batchResult.Results);
        Assert.Equal("Test message", batchResult.Message);
        Assert.Equal("Test data", batchResult.Data);
    }

    [Fact]
    public void FromResults_FactoryMethod_CreatesResult()
    {
        // Arrange
        var results = new List<ProcessingResult> { ProcessingResult.Successful() }.AsReadOnly();

        // Act
        var batchResult = BatchProcessingResult.FromResults(results);

        // Assert
        Assert.Same(results, batchResult.Results);
        Assert.Null(batchResult.Message);
        Assert.Null(batchResult.Data);
    }

    [Fact]
    public void GetResult_WithValidIndex_ReturnsResult()
    {
        // Arrange
        var result1 = ProcessingResult.Successful("First");
        var result2 = ProcessingResult.Failed(new Exception("Second"));
        var results = new List<ProcessingResult> { result1, result2 }.AsReadOnly();
        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var retrieved = batchResult.GetResult(1);

        // Assert
        Assert.Equal(result2, retrieved);
    }

    [Fact]
    public void GetResult_WithInvalidIndex_ThrowsException()
    {
        // Arrange
        var results = new List<ProcessingResult> { ProcessingResult.Successful() }.AsReadOnly();
        var batchResult = new BatchProcessingResult { Results = results };

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => batchResult.GetResult(5));
    }

    [Fact]
    public void GetFailedResults_ReturnsOnlyFailures()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful("Success 1"),
            ProcessingResult.Failed(new Exception("Error 1")),
            ProcessingResult.Successful("Success 2"),
            ProcessingResult.Failed(new Exception("Error 2"))
        }.AsReadOnly();
        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var failures = batchResult.GetFailedResults().ToList();

        // Assert
        Assert.Equal(2, failures.Count);
        Assert.Equal(1, failures[0].Index);
        Assert.Equal(3, failures[1].Index);
        Assert.False(failures[0].Result.Success);
        Assert.False(failures[1].Result.Success);
    }

    [Fact]
    public void GetSuccessfulResults_ReturnsOnlySuccesses()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful("Success 1"),
            ProcessingResult.Failed(new Exception("Error")),
            ProcessingResult.Successful("Success 2")
        }.AsReadOnly();
        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var successes = batchResult.GetSuccessfulResults().ToList();

        // Assert
        Assert.Equal(2, successes.Count);
        Assert.Equal(0, successes[0].Index);
        Assert.Equal(2, successes[1].Index);
        Assert.True(successes[0].Result.Success);
        Assert.True(successes[1].Result.Success);
    }

    [Fact]
    public void GetFailedResults_WithAllSuccessful_ReturnsEmpty()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful(),
            ProcessingResult.Successful()
        }.AsReadOnly();
        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var failures = batchResult.GetFailedResults();

        // Assert
        Assert.Empty(failures);
    }

    [Fact]
    public void GetSuccessfulResults_WithAllFailed_ReturnsEmpty()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Failed(new Exception()),
            ProcessingResult.Failed(new Exception())
        }.AsReadOnly();
        var batchResult = new BatchProcessingResult { Results = results };

        // Act
        var successes = batchResult.GetSuccessfulResults();

        // Assert
        Assert.Empty(successes);
    }

    [Fact]
    public void EmptyBatch_ReturnsZeroForAllCounts()
    {
        // Arrange
        var results = new List<ProcessingResult>().AsReadOnly();
        var batchResult = new BatchProcessingResult { Results = results };

        // Act & Assert
        Assert.Equal(0, batchResult.TotalCount);
        Assert.Equal(0, batchResult.SuccessCount);
        Assert.Equal(0, batchResult.FailureCount);
        Assert.False(batchResult.AnySucceeded);
        Assert.True(batchResult.AllFailed);
    }

    [Fact]
    public void LargeBatch_CalculatesCorrectly()
    {
        // Arrange
        var results = Enumerable.Range(0, 1000)
            .Select(i => i % 2 == 0 ? ProcessingResult.Successful() : ProcessingResult.Failed(new Exception()))
            .ToList()
            .AsReadOnly();
        var batchResult = new BatchProcessingResult { Results = results };

        // Act & Assert
        Assert.Equal(1000, batchResult.TotalCount);
        Assert.Equal(500, batchResult.SuccessCount);
        Assert.Equal(500, batchResult.FailureCount);
        Assert.True(batchResult.AnySucceeded);
        Assert.False(batchResult.AllSucceeded);
        Assert.False(batchResult.AllFailed);
    }

    [Fact]
    public void StructBehavior_IsValueType()
    {
        // Arrange
        var results = new List<ProcessingResult> { ProcessingResult.Successful() }.AsReadOnly();
        var original = new BatchProcessingResult { Results = results };

        // Act
        var copy = original;

        // Assert
        Assert.Equal(original.TotalCount, copy.TotalCount);
        Assert.Same(original.Results, copy.Results);
    }
}
