using HeroMessaging.Abstractions.Processing;

namespace HeroMessaging.Abstractions.Tests.Processing;

[Trait("Category", "Unit")]
public class BatchProcessingOptionsTests
{
    [Fact]
    public void Constructor_SetsDefaultValues()
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
    public void Enabled_CanBeSet()
    {
        // Arrange
        var options = new BatchProcessingOptions();

        // Act
        options.Enabled = true;

        // Assert
        Assert.True(options.Enabled);
    }

    [Fact]
    public void MaxBatchSize_CanBeSet()
    {
        // Arrange
        var options = new BatchProcessingOptions();

        // Act
        options.MaxBatchSize = 100;

        // Assert
        Assert.Equal(100, options.MaxBatchSize);
    }

    [Fact]
    public void BatchTimeout_CanBeSet()
    {
        // Arrange
        var options = new BatchProcessingOptions();

        // Act
        options.BatchTimeout = TimeSpan.FromSeconds(1);

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), options.BatchTimeout);
    }

    [Fact]
    public void MinBatchSize_CanBeSet()
    {
        // Arrange
        var options = new BatchProcessingOptions();

        // Act
        options.MinBatchSize = 5;

        // Assert
        Assert.Equal(5, options.MinBatchSize);
    }

    [Fact]
    public void FallbackToIndividualProcessing_CanBeDisabled()
    {
        // Arrange
        var options = new BatchProcessingOptions();

        // Act
        options.FallbackToIndividualProcessing = false;

        // Assert
        Assert.False(options.FallbackToIndividualProcessing);
    }

    [Fact]
    public void MaxDegreeOfParallelism_CanBeSet()
    {
        // Arrange
        var options = new BatchProcessingOptions();

        // Act
        options.MaxDegreeOfParallelism = 4;

        // Assert
        Assert.Equal(4, options.MaxDegreeOfParallelism);
    }

    [Fact]
    public void ContinueOnFailure_CanBeDisabled()
    {
        // Arrange
        var options = new BatchProcessingOptions();

        // Act
        options.ContinueOnFailure = false;

        // Assert
        Assert.False(options.ContinueOnFailure);
    }

    [Fact]
    public void Validate_WithValidOptions_DoesNotThrow()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            MaxBatchSize = 100,
            BatchTimeout = TimeSpan.FromMilliseconds(500),
            MinBatchSize = 10,
            MaxDegreeOfParallelism = 4
        };

        // Act & Assert
        options.Validate(); // Should not throw
    }

    [Fact]
    public void Validate_WithZeroMaxBatchSize_ThrowsArgumentException()
    {
        // Arrange
        var options = new BatchProcessingOptions { MaxBatchSize = 0 };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Equal("MaxBatchSize", exception.ParamName);
        Assert.Contains("greater than 0", exception.Message);
    }

    [Fact]
    public void Validate_WithNegativeMaxBatchSize_ThrowsArgumentException()
    {
        // Arrange
        var options = new BatchProcessingOptions { MaxBatchSize = -1 };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Equal("MaxBatchSize", exception.ParamName);
    }

    [Fact]
    public void Validate_WithZeroBatchTimeout_ThrowsArgumentException()
    {
        // Arrange
        var options = new BatchProcessingOptions { BatchTimeout = TimeSpan.Zero };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Equal("BatchTimeout", exception.ParamName);
        Assert.Contains("greater than zero", exception.Message);
    }

    [Fact]
    public void Validate_WithNegativeBatchTimeout_ThrowsArgumentException()
    {
        // Arrange
        var options = new BatchProcessingOptions { BatchTimeout = TimeSpan.FromSeconds(-1) };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Equal("BatchTimeout", exception.ParamName);
    }

    [Fact]
    public void Validate_WithZeroMinBatchSize_ThrowsArgumentException()
    {
        // Arrange
        var options = new BatchProcessingOptions { MinBatchSize = 0 };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Equal("MinBatchSize", exception.ParamName);
        Assert.Contains("at least 1", exception.Message);
    }

    [Fact]
    public void Validate_WithNegativeMinBatchSize_ThrowsArgumentException()
    {
        // Arrange
        var options = new BatchProcessingOptions { MinBatchSize = -1 };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Equal("MinBatchSize", exception.ParamName);
    }

    [Fact]
    public void Validate_WithMinGreaterThanMax_ThrowsArgumentException()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            MaxBatchSize = 10,
            MinBatchSize = 20
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Equal("MinBatchSize", exception.ParamName);
        Assert.Contains("cannot be greater than MaxBatchSize", exception.Message);
    }

    [Fact]
    public void Validate_WithMinEqualToMax_DoesNotThrow()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            MaxBatchSize = 10,
            MinBatchSize = 10
        };

        // Act & Assert
        options.Validate(); // Should not throw
    }

    [Fact]
    public void Validate_WithZeroMaxDegreeOfParallelism_ThrowsArgumentException()
    {
        // Arrange
        var options = new BatchProcessingOptions { MaxDegreeOfParallelism = 0 };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Equal("MaxDegreeOfParallelism", exception.ParamName);
        Assert.Contains("greater than 0", exception.Message);
    }

    [Fact]
    public void Validate_WithNegativeMaxDegreeOfParallelism_ThrowsArgumentException()
    {
        // Arrange
        var options = new BatchProcessingOptions { MaxDegreeOfParallelism = -1 };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Equal("MaxDegreeOfParallelism", exception.ParamName);
    }

    [Fact]
    public void Validate_WithVeryLargeMaxBatchSize_DoesNotThrow()
    {
        // Arrange
        var options = new BatchProcessingOptions { MaxBatchSize = 10000 };

        // Act & Assert
        options.Validate();
    }

    [Fact]
    public void Validate_WithVeryLongBatchTimeout_DoesNotThrow()
    {
        // Arrange
        var options = new BatchProcessingOptions { BatchTimeout = TimeSpan.FromHours(1) };

        // Act & Assert
        options.Validate();
    }

    [Fact]
    public void Validate_WithVeryShortBatchTimeout_DoesNotThrow()
    {
        // Arrange
        var options = new BatchProcessingOptions { BatchTimeout = TimeSpan.FromMilliseconds(1) };

        // Act & Assert
        options.Validate();
    }

    [Fact]
    public void HighThroughputConfiguration_ValidatesCorrectly()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = true,
            MaxBatchSize = 1000,
            BatchTimeout = TimeSpan.FromSeconds(1),
            MinBatchSize = 100,
            MaxDegreeOfParallelism = 8,
            ContinueOnFailure = true
        };

        // Act & Assert
        options.Validate();
        Assert.True(options.Enabled);
        Assert.Equal(1000, options.MaxBatchSize);
        Assert.Equal(8, options.MaxDegreeOfParallelism);
    }

    [Fact]
    public void LowLatencyConfiguration_ValidatesCorrectly()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = true,
            MaxBatchSize = 10,
            BatchTimeout = TimeSpan.FromMilliseconds(100),
            MinBatchSize = 2,
            MaxDegreeOfParallelism = 1,
            FallbackToIndividualProcessing = true
        };

        // Act & Assert
        options.Validate();
        Assert.True(options.Enabled);
        Assert.Equal(10, options.MaxBatchSize);
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.BatchTimeout);
    }

    [Fact]
    public void SequentialProcessingConfiguration_HasParallelismOfOne()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            MaxDegreeOfParallelism = 1
        };

        // Act & Assert
        Assert.Equal(1, options.MaxDegreeOfParallelism);
        options.Validate();
    }

    [Fact]
    public void ParallelProcessingConfiguration_HasHigherParallelism()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        // Act & Assert
        Assert.True(options.MaxDegreeOfParallelism > 0);
        options.Validate();
    }

    [Fact]
    public void DisabledConfiguration_BypassesBatchProcessing()
    {
        // Arrange
        var options = new BatchProcessingOptions
        {
            Enabled = false
        };

        // Act & Assert
        Assert.False(options.Enabled);
        options.Validate();
    }
}
