using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Configuration;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

/// <summary>
/// Unit tests for BatchProcessingBuilder and related extensions.
/// </summary>
[Trait("Category", "Unit")]
public class BatchProcessingBuilderTests
{
    private readonly ServiceCollection _services;
    private readonly HeroMessagingBuilder _builder;

    public BatchProcessingBuilderTests()
    {
        _services = new ServiceCollection();
        _services.AddLogging();
        _builder = new HeroMessagingBuilder(_services);
    }

    #region WithBatchProcessing Extension Tests

    [Fact]
    public void WithBatchProcessing_WithDefaultConfiguration_RegistersBatchProcessingOptions()
    {
        // Act
        _builder.WithBatchProcessing();
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<BatchProcessingOptions>();
        Assert.NotNull(options);
        Assert.False(options.Enabled);
        Assert.Equal(50, options.MaxBatchSize);
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.BatchTimeout);
        Assert.Equal(2, options.MinBatchSize);
        Assert.Equal(1, options.MaxDegreeOfParallelism);
        Assert.True(options.ContinueOnFailure);
        Assert.True(options.FallbackToIndividualProcessing);
    }

    [Fact]
    public void WithBatchProcessing_WithCustomConfiguration_AppliesSettings()
    {
        // Act
        _builder.WithBatchProcessing(batch =>
        {
            batch.Enable()
                 .WithMaxBatchSize(100)
                 .WithBatchTimeout(TimeSpan.FromMilliseconds(500))
                 .WithMinBatchSize(10)
                 .WithParallelProcessing(4)
                 .WithContinueOnFailure(false)
                 .WithFallbackToIndividual(false);
        });
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<BatchProcessingOptions>();
        Assert.True(options.Enabled);
        Assert.Equal(100, options.MaxBatchSize);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.BatchTimeout);
        Assert.Equal(10, options.MinBatchSize);
        Assert.Equal(4, options.MaxDegreeOfParallelism);
        Assert.False(options.ContinueOnFailure);
        Assert.False(options.FallbackToIndividualProcessing);
    }

    [Fact]
    public void WithBatchProcessing_RegistersBatchDecoratorFactory()
    {
        // Act
        _builder.WithBatchProcessing(batch => batch.Enable());
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var factory = provider.GetService<Func<IMessageProcessor, BatchDecorator>>();
        Assert.NotNull(factory);
    }

    [Fact]
    public void WithBatchProcessing_RegistersTimeProvider()
    {
        // Act
        _builder.WithBatchProcessing();
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var timeProvider = provider.GetService<TimeProvider>();
        Assert.NotNull(timeProvider);
    }

    [Fact]
    public void WithBatchProcessing_ReturnsSameBuilder_ForMethodChaining()
    {
        // Act
        var result = _builder.WithBatchProcessing();

        // Assert
        Assert.Same(_builder, result);
    }

    #endregion

    #region Enable Tests

    [Fact]
    public void Enable_WithTrue_SetsEnabledToTrue()
    {
        // Act
        _builder.WithBatchProcessing(batch => batch.Enable(true));
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<BatchProcessingOptions>();
        Assert.True(options.Enabled);
    }

    [Fact]
    public void Enable_WithFalse_SetsEnabledToFalse()
    {
        // Act
        _builder.WithBatchProcessing(batch => batch.Enable(false));
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<BatchProcessingOptions>();
        Assert.False(options.Enabled);
    }

    [Fact]
    public void Enable_WithNoParameter_DefaultsToTrue()
    {
        // Act
        _builder.WithBatchProcessing(batch => batch.Enable());
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<BatchProcessingOptions>();
        Assert.True(options.Enabled);
    }

    #endregion

    #region WithMaxBatchSize Tests

    [Fact]
    public void WithMaxBatchSize_WithValidValue_SetsMaxBatchSize()
    {
        // Act
        _builder.WithBatchProcessing(batch => batch.WithMaxBatchSize(200));
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<BatchProcessingOptions>();
        Assert.Equal(200, options.MaxBatchSize);
    }

    [Fact]
    public void WithMaxBatchSize_WithZero_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.WithBatchProcessing(batch => batch.WithMaxBatchSize(0)));
        Assert.Contains("MaxBatchSize must be greater than 0", exception.Message);
    }

    [Fact]
    public void WithMaxBatchSize_WithNegativeValue_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.WithBatchProcessing(batch => batch.WithMaxBatchSize(-1)));
        Assert.Contains("MaxBatchSize must be greater than 0", exception.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(1000)]
    public void WithMaxBatchSize_WithVariousValidValues_SetsCorrectly(int value)
    {
        // Act - also set MinBatchSize=1 to ensure validation passes when MaxBatchSize is small
        _builder.WithBatchProcessing(batch => batch.WithMaxBatchSize(value).WithMinBatchSize(1));
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<BatchProcessingOptions>();
        Assert.Equal(value, options.MaxBatchSize);
    }

    #endregion

    #region WithBatchTimeout Tests

    [Fact]
    public void WithBatchTimeout_WithValidValue_SetsBatchTimeout()
    {
        // Act
        _builder.WithBatchProcessing(batch => batch.WithBatchTimeout(TimeSpan.FromSeconds(1)));
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<BatchProcessingOptions>();
        Assert.Equal(TimeSpan.FromSeconds(1), options.BatchTimeout);
    }

    [Fact]
    public void WithBatchTimeout_WithZero_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.WithBatchProcessing(batch => batch.WithBatchTimeout(TimeSpan.Zero)));
        Assert.Contains("BatchTimeout must be greater than zero", exception.Message);
    }

    [Fact]
    public void WithBatchTimeout_WithNegativeValue_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.WithBatchProcessing(batch => batch.WithBatchTimeout(TimeSpan.FromMilliseconds(-100))));
        Assert.Contains("BatchTimeout must be greater than zero", exception.Message);
    }

    #endregion

    #region WithMinBatchSize Tests

    [Fact]
    public void WithMinBatchSize_WithValidValue_SetsMinBatchSize()
    {
        // Act
        _builder.WithBatchProcessing(batch => batch.WithMinBatchSize(5));
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<BatchProcessingOptions>();
        Assert.Equal(5, options.MinBatchSize);
    }

    [Fact]
    public void WithMinBatchSize_WithZero_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.WithBatchProcessing(batch => batch.WithMinBatchSize(0)));
        Assert.Contains("MinBatchSize must be at least 1", exception.Message);
    }

    [Fact]
    public void WithMinBatchSize_WithNegativeValue_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.WithBatchProcessing(batch => batch.WithMinBatchSize(-1)));
        Assert.Contains("MinBatchSize must be at least 1", exception.Message);
    }

    #endregion

    #region WithParallelProcessing Tests

    [Fact]
    public void WithParallelProcessing_WithValidValue_SetsMaxDegreeOfParallelism()
    {
        // Act
        _builder.WithBatchProcessing(batch => batch.WithParallelProcessing(8));
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<BatchProcessingOptions>();
        Assert.Equal(8, options.MaxDegreeOfParallelism);
    }

    [Fact]
    public void WithParallelProcessing_WithZero_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.WithBatchProcessing(batch => batch.WithParallelProcessing(0)));
        Assert.Contains("MaxDegreeOfParallelism must be greater than 0", exception.Message);
    }

    [Fact]
    public void WithParallelProcessing_WithNegativeValue_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.WithBatchProcessing(batch => batch.WithParallelProcessing(-1)));
        Assert.Contains("MaxDegreeOfParallelism must be greater than 0", exception.Message);
    }

    #endregion

    #region Profile Tests

    [Fact]
    public void UseHighThroughputProfile_SetsExpectedValues()
    {
        // Act
        _builder.WithBatchProcessing(batch => batch.UseHighThroughputProfile());
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<BatchProcessingOptions>();
        Assert.True(options.Enabled);
        Assert.Equal(100, options.MaxBatchSize);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.BatchTimeout);
        Assert.Equal(10, options.MinBatchSize);
        Assert.Equal(4, options.MaxDegreeOfParallelism);
    }

    [Fact]
    public void UseLowLatencyProfile_SetsExpectedValues()
    {
        // Act
        _builder.WithBatchProcessing(batch => batch.UseLowLatencyProfile());
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<BatchProcessingOptions>();
        Assert.True(options.Enabled);
        Assert.Equal(20, options.MaxBatchSize);
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.BatchTimeout);
        Assert.Equal(5, options.MinBatchSize);
        Assert.Equal(1, options.MaxDegreeOfParallelism);
    }

    [Fact]
    public void UseBalancedProfile_SetsExpectedValues()
    {
        // Act
        _builder.WithBatchProcessing(batch => batch.UseBalancedProfile());
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<BatchProcessingOptions>();
        Assert.True(options.Enabled);
        Assert.Equal(50, options.MaxBatchSize);
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.BatchTimeout);
        Assert.Equal(2, options.MinBatchSize);
        Assert.Equal(2, options.MaxDegreeOfParallelism);
    }

    [Fact]
    public void Profile_CanBeOverriddenWithCustomValues()
    {
        // Act
        _builder.WithBatchProcessing(batch =>
        {
            batch.UseHighThroughputProfile()
                 .WithMaxBatchSize(200)
                 .WithParallelProcessing(8);
        });
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<BatchProcessingOptions>();
        Assert.True(options.Enabled);  // From profile
        Assert.Equal(200, options.MaxBatchSize);  // Overridden
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.BatchTimeout);  // From profile
        Assert.Equal(10, options.MinBatchSize);  // From profile
        Assert.Equal(8, options.MaxDegreeOfParallelism);  // Overridden
    }

    #endregion

    #region Method Chaining Tests

    [Fact]
    public void AllMethods_SupportFluentChaining()
    {
        // Act & Assert - Should not throw
        _builder.WithBatchProcessing(batch =>
        {
            batch.Enable()
                 .WithMaxBatchSize(100)
                 .WithBatchTimeout(TimeSpan.FromMilliseconds(500))
                 .WithMinBatchSize(10)
                 .WithParallelProcessing(4)
                 .WithContinueOnFailure(true)
                 .WithFallbackToIndividual(true);
        });
    }

    #endregion

    #region BatchDecorator Factory Tests

    [Fact]
    public void BatchDecoratorFactory_CreatesDecoratorWithCorrectOptions()
    {
        // Arrange
        var innerProcessor = new Mock<IMessageProcessor>().Object;

        // Act
        _builder.WithBatchProcessing(batch => batch.Enable().WithMaxBatchSize(75));
        _builder.Build();
        var provider = _services.BuildServiceProvider();
        var factory = provider.GetRequiredService<Func<IMessageProcessor, BatchDecorator>>();
        var decorator = factory(innerProcessor);

        // Assert
        Assert.NotNull(decorator);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void Build_WithMinBatchSizeGreaterThanMaxBatchSize_ThrowsOnValidation()
    {
        // Act
        _builder.WithBatchProcessing(batch =>
        {
            batch.WithMaxBatchSize(5)
                 .WithMinBatchSize(10);  // Invalid: min > max
        });
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert - validation happens when options are resolved (throws ArgumentException)
        Assert.Throws<ArgumentException>(() => provider.GetRequiredService<BatchProcessingOptions>());
    }

    #endregion
}
