using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Configuration;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroMessaging.Tests.Unit;

/// <summary>
/// Unit tests for batch processing builder extensions
/// </summary>
public class BatchProcessingBuilderTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithBatchProcessing(batch => batch.Enable());
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<BatchProcessingOptions>();
        Assert.NotNull(options);
        Assert.True(options.Enabled);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_WithNullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        IHeroMessagingBuilder builder = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.WithBatchProcessing());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_WithDefaultConfiguration_UsesDefaults()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithBatchProcessing();
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BatchProcessingOptions>();

        // Assert
        Assert.False(options.Enabled); // Disabled by default
        Assert.Equal(50, options.MaxBatchSize);
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.BatchTimeout);
        Assert.Equal(2, options.MinBatchSize);
        Assert.Equal(1, options.MaxDegreeOfParallelism);
        Assert.True(options.ContinueOnFailure);
        Assert.True(options.FallbackToIndividualProcessing);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_WithCustomConfiguration_UsesCustomValues()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithBatchProcessing(batch =>
        {
            batch
                .Enable()
                .WithMaxBatchSize(100)
                .WithBatchTimeout(TimeSpan.FromSeconds(1))
                .WithMinBatchSize(10)
                .WithParallelProcessing(4)
                .WithContinueOnFailure(false)
                .WithFallbackToIndividual(false);
        });
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BatchProcessingOptions>();

        // Assert
        Assert.True(options.Enabled);
        Assert.Equal(100, options.MaxBatchSize);
        Assert.Equal(TimeSpan.FromSeconds(1), options.BatchTimeout);
        Assert.Equal(10, options.MinBatchSize);
        Assert.Equal(4, options.MaxDegreeOfParallelism);
        Assert.False(options.ContinueOnFailure);
        Assert.False(options.FallbackToIndividualProcessing);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_UseHighThroughputProfile_SetsExpectedValues()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithBatchProcessing(batch => batch.UseHighThroughputProfile());
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BatchProcessingOptions>();

        // Assert
        Assert.True(options.Enabled);
        Assert.Equal(100, options.MaxBatchSize);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.BatchTimeout);
        Assert.Equal(10, options.MinBatchSize);
        Assert.Equal(4, options.MaxDegreeOfParallelism);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_UseLowLatencyProfile_SetsExpectedValues()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithBatchProcessing(batch => batch.UseLowLatencyProfile());
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BatchProcessingOptions>();

        // Assert
        Assert.True(options.Enabled);
        Assert.Equal(20, options.MaxBatchSize);
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.BatchTimeout);
        Assert.Equal(5, options.MinBatchSize);
        Assert.Equal(1, options.MaxDegreeOfParallelism);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_UseBalancedProfile_SetsExpectedValues()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithBatchProcessing(batch => batch.UseBalancedProfile());
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BatchProcessingOptions>();

        // Assert
        Assert.True(options.Enabled);
        Assert.Equal(50, options.MaxBatchSize);
        Assert.Equal(TimeSpan.FromMilliseconds(200), options.BatchTimeout);
        Assert.Equal(2, options.MinBatchSize);
        Assert.Equal(2, options.MaxDegreeOfParallelism);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_WithInvalidMaxBatchSize_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
        {
            builder.WithBatchProcessing(batch => batch.WithMaxBatchSize(0));
        });
        Assert.Contains("MaxBatchSize must be greater than 0", exception.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_WithInvalidBatchTimeout_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
        {
            builder.WithBatchProcessing(batch => batch.WithBatchTimeout(TimeSpan.Zero));
        });
        Assert.Contains("BatchTimeout must be greater than zero", exception.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_WithInvalidMinBatchSize_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
        {
            builder.WithBatchProcessing(batch => batch.WithMinBatchSize(0));
        });
        Assert.Contains("MinBatchSize must be at least 1", exception.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_WithInvalidParallelism_ThrowsArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
        {
            builder.WithBatchProcessing(batch => batch.WithParallelProcessing(0));
        });
        Assert.Contains("MaxDegreeOfParallelism must be greater than 0", exception.Message);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_MinGreaterThanMax_ThrowsArgumentException()
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
    public void WithBatchProcessing_RegistersBatchDecoratorFactory()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithBatchProcessing(batch => batch.Enable());
        var provider = services.BuildServiceProvider();

        // Assert
        var factory = provider.GetService<Func<IMessageProcessor, BatchDecorator>>();
        Assert.NotNull(factory);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_RegistersTimeProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithBatchProcessing();
        var provider = services.BuildServiceProvider();

        // Assert
        var timeProvider = provider.GetService<TimeProvider>();
        Assert.NotNull(timeProvider);
        Assert.Equal(TimeProvider.System, timeProvider);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_Enable_CanBeDisabled()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        builder.WithBatchProcessing(batch => batch.Enable(false));
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BatchProcessingOptions>();

        // Assert
        Assert.False(options.Enabled);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_ReturnsBuilderForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder.WithBatchProcessing();

        // Assert
        Assert.Same(builder, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WithBatchProcessing_FluentApi_AllowsMethodChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new HeroMessagingBuilder(services);

        // Act
        var result = builder
            .WithBatchProcessing(batch => batch
                .Enable()
                .WithMaxBatchSize(100)
                .WithBatchTimeout(TimeSpan.FromSeconds(1))
                .WithMinBatchSize(10));

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<BatchProcessingOptions>();

        // Assert
        Assert.Same(builder, result);
        Assert.True(options.Enabled);
        Assert.Equal(100, options.MaxBatchSize);
    }
}
