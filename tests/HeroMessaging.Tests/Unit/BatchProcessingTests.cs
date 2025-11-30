using HeroMessaging.Abstractions.Processing;
using Xunit;

namespace HeroMessaging.Tests.Unit;

/// <summary>
/// Unit tests for batch processing abstractions
/// </summary>
public class BatchProcessingTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingResult_Create_ReturnsValidResult()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful("Message 1"),
            ProcessingResult.Successful("Message 2"),
            ProcessingResult.Failed(new Exception("Error"), "Message 3")
        };

        // Act
        var batchResult = BatchProcessingResult.Create(results, "Test batch", null);

        // Assert
        Assert.Equal(3, batchResult.TotalCount);
        Assert.Equal(2, batchResult.SuccessCount);
        Assert.Equal(1, batchResult.FailureCount);
        Assert.False(batchResult.AllSucceeded);
        Assert.True(batchResult.AnySucceeded);
        Assert.False(batchResult.AllFailed);
        Assert.Equal("Test batch", batchResult.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingResult_FromResults_ReturnsValidResult()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful(),
            ProcessingResult.Successful()
        };

        // Act
        var batchResult = BatchProcessingResult.FromResults(results);

        // Assert
        Assert.Equal(2, batchResult.TotalCount);
        Assert.Equal(2, batchResult.SuccessCount);
        Assert.Equal(0, batchResult.FailureCount);
        Assert.True(batchResult.AllSucceeded);
        Assert.True(batchResult.AnySucceeded);
        Assert.False(batchResult.AllFailed);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingResult_AllFailed_WhenAllResultsFailed()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Failed(new Exception("Error 1")),
            ProcessingResult.Failed(new Exception("Error 2"))
        };

        // Act
        var batchResult = BatchProcessingResult.FromResults(results);

        // Assert
        Assert.Equal(2, batchResult.TotalCount);
        Assert.Equal(0, batchResult.SuccessCount);
        Assert.Equal(2, batchResult.FailureCount);
        Assert.False(batchResult.AllSucceeded);
        Assert.False(batchResult.AnySucceeded);
        Assert.True(batchResult.AllFailed);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingResult_GetResult_ReturnsCorrectResult()
    {
        // Arrange
        var result1 = ProcessingResult.Successful("Data 1");
        var result2 = ProcessingResult.Failed(new Exception("Error"), "Failed");
        var results = new List<ProcessingResult> { result1, result2 };
        var batchResult = BatchProcessingResult.FromResults(results);

        // Act
        var retrieved1 = batchResult.GetResult(0);
        var retrieved2 = batchResult.GetResult(1);

        // Assert
        Assert.True(retrieved1.Success);
        Assert.False(retrieved2.Success);
        Assert.Equal("Data 1", retrieved1.Message);
        Assert.Equal("Failed", retrieved2.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingResult_GetResult_ThrowsOnInvalidIndex()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful()
        };
        var batchResult = BatchProcessingResult.FromResults(results);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => batchResult.GetResult(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => batchResult.GetResult(-1));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingResult_GetFailedResults_ReturnsOnlyFailures()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful("Success 1"),
            ProcessingResult.Failed(new Exception("Error 1"), "Failed 1"),
            ProcessingResult.Successful("Success 2"),
            ProcessingResult.Failed(new Exception("Error 2"), "Failed 2")
        };
        var batchResult = BatchProcessingResult.FromResults(results);

        // Act
        var failures = batchResult.GetFailedResults().ToList();

        // Assert
        Assert.Equal(2, failures.Count);
        Assert.Equal(1, failures[0].Index);
        Assert.Equal("Failed 1", failures[0].Result.Message);
        Assert.Equal(3, failures[1].Index);
        Assert.Equal("Failed 2", failures[1].Result.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingResult_GetSuccessfulResults_ReturnsOnlySuccesses()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful("Success 1"),
            ProcessingResult.Failed(new Exception("Error")),
            ProcessingResult.Successful("Success 2")
        };
        var batchResult = BatchProcessingResult.FromResults(results);

        // Act
        var successes = batchResult.GetSuccessfulResults().ToList();

        // Assert
        Assert.Equal(2, successes.Count);
        Assert.Equal(0, successes[0].Index);
        Assert.Equal("Success 1", successes[0].Result.Message);
        Assert.Equal(2, successes[1].Index);
        Assert.Equal("Success 2", successes[1].Result.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingOptions_DefaultValues_AreValid()
    {
        // Arrange & Act
        var options = new BatchProcessingOptions();

        // Assert
        Assert.False(options.Enabled);
        Assert.Equal(50, options.MaxBatchSize);
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.BatchTimeout);
        Assert.Equal(2, options.MinBatchSize);
        Assert.True(options.FallbackToIndividualProcessing);
        Assert.Equal(1, options.MaxDegreeOfParallelism);
        Assert.True(options.ContinueOnFailure);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingOptions_Validate_SucceedsWithValidOptions()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            MaxBatchSize = 100,
            BatchTimeout = TimeSpan.FromSeconds(1),
            MinBatchSize = 10,
            MaxDegreeOfParallelism = 4
        };

        // Act & Assert
        options.Validate(); // Should not throw
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingOptions_Validate_ThrowsOnInvalidMaxBatchSize()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            MaxBatchSize = 0
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Contains("MaxBatchSize must be greater than 0", exception.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingOptions_Validate_ThrowsOnInvalidBatchTimeout()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            BatchTimeout = TimeSpan.Zero
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Contains("BatchTimeout must be greater than zero", exception.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingOptions_Validate_ThrowsOnInvalidMinBatchSize()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            MinBatchSize = 0
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Contains("MinBatchSize must be at least 1", exception.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingOptions_Validate_ThrowsWhenMinGreaterThanMax()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            MaxBatchSize = 10,
            MinBatchSize = 20
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Contains("MinBatchSize cannot be greater than MaxBatchSize", exception.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingOptions_Validate_ThrowsOnInvalidMaxDegreeOfParallelism()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            MaxDegreeOfParallelism = 0
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Contains("MaxDegreeOfParallelism must be greater than 0", exception.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingResult_EmptyResults_ReturnsZeroCounts()
    {
        // Arrange
        var results = new List<ProcessingResult>();

        // Act
        var batchResult = BatchProcessingResult.FromResults(results);

        // Assert
        Assert.Equal(0, batchResult.TotalCount);
        Assert.Equal(0, batchResult.SuccessCount);
        Assert.Equal(0, batchResult.FailureCount);
        Assert.True(batchResult.AllSucceeded); // Vacuous truth: all of zero succeeded
        Assert.False(batchResult.AnySucceeded);
        Assert.True(batchResult.AllFailed); // Vacuous truth: all of zero failed (0 succeeded)
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BatchProcessingResult_WithData_StoresDataCorrectly()
    {
        // Arrange
        var results = new List<ProcessingResult>
        {
            ProcessingResult.Successful()
        };
        var testData = new { BatchId = 123, ProcessedAt = DateTimeOffset.UtcNow };

        // Act
        var batchResult = BatchProcessingResult.Create(results, "Test", testData);

        // Assert
        Assert.NotNull(batchResult.Data);
        Assert.Same(testData, batchResult.Data);
    }
}
