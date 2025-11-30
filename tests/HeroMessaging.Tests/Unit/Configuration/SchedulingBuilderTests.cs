using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Abstractions.Scheduling;
using HeroMessaging.Configuration;
using HeroMessaging.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

/// <summary>
/// Unit tests for SchedulingBuilder and related extensions.
/// </summary>
[Trait("Category", "Unit")]
public class SchedulingBuilderTests
{
    private readonly ServiceCollection _services;
    private readonly HeroMessagingBuilder _builder;

    public SchedulingBuilderTests()
    {
        _services = new ServiceCollection();
        _services.AddLogging();
        // Add mock dependencies required by HeroMessagingService
        _services.AddSingleton(Mock.Of<ICommandProcessor>());
        _services.AddSingleton(Mock.Of<IQueryProcessor>());
        _services.AddSingleton(Mock.Of<IEventBus>());
        _builder = new HeroMessagingBuilder(_services);
    }

    #region WithScheduling Extension Tests

    [Fact]
    public void WithScheduling_WithNullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        IHeroMessagingBuilder? builder = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => builder!.WithScheduling());
        Assert.Equal("builder", exception.ParamName);
    }

    [Fact]
    public void WithScheduling_WithDefaultConfiguration_RegistersDefaultServices()
    {
        // Act
        _builder.WithScheduling();
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var scheduler = provider.GetService<IMessageScheduler>();
        var deliveryHandler = provider.GetService<IMessageDeliveryHandler>();
        var storage = provider.GetService<IScheduledMessageStorage>();

        Assert.NotNull(scheduler);
        Assert.NotNull(deliveryHandler);
        Assert.NotNull(storage);
        Assert.IsType<InMemoryScheduler>(scheduler);
        Assert.IsType<DefaultMessageDeliveryHandler>(deliveryHandler);
        Assert.IsType<InMemoryScheduledMessageStorage>(storage);
    }

    [Fact]
    public void WithScheduling_ReturnsSameBuilder_ForMethodChaining()
    {
        // Act
        var result = _builder.WithScheduling();

        // Assert
        Assert.Same(_builder, result);
    }

    [Fact]
    public void WithScheduling_WithCustomConfiguration_InvokesConfigureAction()
    {
        // Arrange
        var configureInvoked = false;

        // Act
        _builder.WithScheduling(scheduling =>
        {
            configureInvoked = true;
        });

        // Assert
        Assert.True(configureInvoked);
    }

    #endregion

    #region UseInMemoryScheduler Tests

    [Fact]
    public void UseInMemoryScheduler_WithNullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        ISchedulingBuilder? builder = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => builder!.UseInMemoryScheduler());
        Assert.Equal("builder", exception.ParamName);
    }

    [Fact]
    public void UseInMemoryScheduler_RegistersInMemoryScheduler()
    {
        // Act
        _builder.WithScheduling(scheduling => scheduling.UseInMemoryScheduler());
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var scheduler = provider.GetRequiredService<IMessageScheduler>();
        Assert.IsType<InMemoryScheduler>(scheduler);
    }

    [Fact]
    public void UseInMemoryScheduler_ReturnsSchedulingBuilder_ForChaining()
    {
        // Arrange
        ISchedulingBuilder? capturedBuilder = null;

        // Act
        _builder.WithScheduling(scheduling =>
        {
            var result = scheduling.UseInMemoryScheduler();
            capturedBuilder = result;
        });

        // Assert
        Assert.NotNull(capturedBuilder);
    }

    #endregion

    #region UseStorageBackedScheduler Tests

    [Fact]
    public void UseStorageBackedScheduler_WithNullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        ISchedulingBuilder? builder = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => builder!.UseStorageBackedScheduler());
        Assert.Equal("builder", exception.ParamName);
    }

    [Fact]
    public void UseStorageBackedScheduler_RegistersStorageBackedScheduler()
    {
        // Act
        _builder.WithScheduling(scheduling => scheduling.UseStorageBackedScheduler());
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var scheduler = provider.GetRequiredService<IMessageScheduler>();
        Assert.IsType<StorageBackedScheduler>(scheduler);
    }

    [Fact]
    public void UseStorageBackedScheduler_WithDefaultOptions_RegistersDefaultOptions()
    {
        // Act
        _builder.WithScheduling(scheduling => scheduling.UseStorageBackedScheduler());
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<StorageBackedSchedulerOptions>();
        Assert.Equal(TimeSpan.FromSeconds(1), options.PollingInterval);
        Assert.Equal(100, options.BatchSize);
        Assert.Equal(10, options.MaxConcurrency);
        Assert.True(options.AutoCleanup);
        Assert.Equal(TimeSpan.FromHours(24), options.CleanupAge);
        Assert.Equal(TimeSpan.FromHours(1), options.CleanupInterval);
    }

    [Fact]
    public void UseStorageBackedScheduler_WithCustomOptions_AppliesCustomOptions()
    {
        // Act
        _builder.WithScheduling(scheduling =>
        {
            scheduling.UseStorageBackedScheduler(options =>
            {
                options.PollingInterval = TimeSpan.FromSeconds(5);
                options.BatchSize = 50;
                options.MaxConcurrency = 20;
                options.AutoCleanup = false;
                options.CleanupAge = TimeSpan.FromHours(48);
                options.CleanupInterval = TimeSpan.FromHours(2);
            });
        });
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<StorageBackedSchedulerOptions>();
        Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
        Assert.Equal(50, options.BatchSize);
        Assert.Equal(20, options.MaxConcurrency);
        Assert.False(options.AutoCleanup);
        Assert.Equal(TimeSpan.FromHours(48), options.CleanupAge);
        Assert.Equal(TimeSpan.FromHours(2), options.CleanupInterval);
    }

    [Fact]
    public void UseStorageBackedScheduler_ReturnsSchedulingBuilder_ForChaining()
    {
        // Arrange
        ISchedulingBuilder? capturedBuilder = null;

        // Act
        _builder.WithScheduling(scheduling =>
        {
            var result = scheduling.UseStorageBackedScheduler();
            capturedBuilder = result;
        });

        // Assert
        Assert.NotNull(capturedBuilder);
    }

    [Fact]
    public void UseStorageBackedScheduler_EnsuresStorageIsRegistered()
    {
        // Act
        _builder.WithScheduling(scheduling => scheduling.UseStorageBackedScheduler());
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert
        var storage = provider.GetService<IScheduledMessageStorage>();
        Assert.NotNull(storage);
        Assert.IsType<InMemoryScheduledMessageStorage>(storage);
    }

    #endregion

    #region StorageBackedSchedulerOptions Tests

    [Fact]
    public void StorageBackedSchedulerOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new StorageBackedSchedulerOptions();

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), options.PollingInterval);
        Assert.Equal(100, options.BatchSize);
        Assert.Equal(10, options.MaxConcurrency);
        Assert.True(options.AutoCleanup);
        Assert.Equal(TimeSpan.FromHours(24), options.CleanupAge);
        Assert.Equal(TimeSpan.FromHours(1), options.CleanupInterval);
    }

    [Fact]
    public void StorageBackedSchedulerOptions_AllProperties_CanBeSet()
    {
        // Arrange
        var options = new StorageBackedSchedulerOptions
        {
            // Act
            PollingInterval = TimeSpan.FromMilliseconds(500),
            BatchSize = 200,
            MaxConcurrency = 50,
            AutoCleanup = false,
            CleanupAge = TimeSpan.FromDays(7),
            CleanupInterval = TimeSpan.FromMinutes(30)
        };

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.PollingInterval);
        Assert.Equal(200, options.BatchSize);
        Assert.Equal(50, options.MaxConcurrency);
        Assert.False(options.AutoCleanup);
        Assert.Equal(TimeSpan.FromDays(7), options.CleanupAge);
        Assert.Equal(TimeSpan.FromMinutes(30), options.CleanupInterval);
    }

    #endregion

    #region SchedulingBuilder Tests

    [Fact]
    public void SchedulingBuilder_Services_ExposesServiceCollection()
    {
        // Arrange
        ISchedulingBuilder? schedulingBuilder = null;

        // Act
        _builder.WithScheduling(scheduling =>
        {
            schedulingBuilder = scheduling;
        });

        // Assert
        Assert.NotNull(schedulingBuilder);
        Assert.Same(_services, schedulingBuilder.Services);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void WithScheduling_WithFullConfiguration_RegistersAllServices()
    {
        // Act
        _builder.WithScheduling(scheduling =>
        {
            scheduling.UseStorageBackedScheduler(options =>
            {
                options.PollingInterval = TimeSpan.FromSeconds(2);
                options.BatchSize = 50;
            });
        });
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert - All services should be registered and resolvable
        Assert.NotNull(provider.GetRequiredService<IMessageScheduler>());
        Assert.NotNull(provider.GetRequiredService<IMessageDeliveryHandler>());
        Assert.NotNull(provider.GetRequiredService<IScheduledMessageStorage>());
        Assert.NotNull(provider.GetRequiredService<StorageBackedSchedulerOptions>());
    }

    [Fact]
    public void WithScheduling_CalledMultipleTimes_UsesLastSchedulerConfiguration()
    {
        // Act - First call uses InMemory, second overrides with StorageBacked
        _builder.WithScheduling(scheduling =>
        {
            scheduling.UseInMemoryScheduler();
            scheduling.UseStorageBackedScheduler();
        });
        _builder.Build();
        var provider = _services.BuildServiceProvider();

        // Assert - StorageBackedScheduler should be registered (last wins)
        var scheduler = provider.GetRequiredService<IMessageScheduler>();
        Assert.IsType<StorageBackedScheduler>(scheduler);
    }

    #endregion
}
