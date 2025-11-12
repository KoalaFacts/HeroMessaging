using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Scheduling;
using HeroMessaging.Configuration;
using HeroMessaging.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Configuration.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ExtensionsToSchedulingTests
{
    private IHeroMessagingBuilder CreateBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        return new HeroMessagingBuilder(services);
    }

    [Fact]
    public void WithScheduling_WithoutConfiguration_RegistersDefaultComponents()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var result = builder.WithScheduling();

        // Assert
        Assert.NotNull(result);
        Assert.Same(builder, result);

        var services = builder.Build();
        Assert.Contains(services, sd => sd.ServiceType == typeof(IMessageDeliveryHandler));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IScheduledMessageStorage));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IMessageScheduler));
    }

    [Fact]
    public void WithScheduling_WithConfiguration_ExecutesConfiguration()
    {
        // Arrange
        var builder = CreateBuilder();
        var configureWasCalled = false;

        // Act
        var result = builder.WithScheduling(scheduling =>
        {
            configureWasCalled = true;
        });

        // Assert
        Assert.True(configureWasCalled);
        Assert.Same(builder, result);
    }

    [Fact]
    public void WithScheduling_WithNullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        IHeroMessagingBuilder? builder = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder!.WithScheduling());
    }

    [Fact]
    public void WithScheduling_ReturnsBuilder_ForFluentAPI()
    {
        // Arrange
        var builder = CreateBuilder();

        // Act
        var result = builder.WithScheduling();

        // Assert
        Assert.IsAssignableFrom<IHeroMessagingBuilder>(result);
    }

    [Fact]
    public void UseInMemoryScheduler_RegistersInMemoryScheduler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IMessageDeliveryHandler>(sp => null!); // Mock
        var schedulingBuilder = new SchedulingBuilderMock(services);

        // Act
        var result = schedulingBuilder.UseInMemoryScheduler();

        // Assert
        Assert.NotNull(result);
        Assert.Same(schedulingBuilder, result);
    }

    [Fact]
    public void UseInMemoryScheduler_WithNullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        ISchedulingBuilder? builder = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder!.UseInMemoryScheduler());
    }

    [Fact]
    public void UseStorageBackedScheduler_WithoutOptions_RegistersScheduler()
    {
        // Arrange
        var services = new ServiceCollection();
        var schedulingBuilder = new SchedulingBuilderMock(services);

        // Act
        var result = schedulingBuilder.UseStorageBackedScheduler();

        // Assert
        Assert.NotNull(result);
        Assert.Same(schedulingBuilder, result);

        var provider = services.BuildServiceProvider();
        var options = provider.GetService<StorageBackedSchedulerOptions>();
        Assert.NotNull(options);
        Assert.Equal(TimeSpan.FromSeconds(1), options.PollingInterval);
        Assert.Equal(100, options.BatchSize);
        Assert.Equal(10, options.MaxConcurrency);
    }

    [Fact]
    public void UseStorageBackedScheduler_WithOptions_AppliesConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var schedulingBuilder = new SchedulingBuilderMock(services);

        // Act
        var result = schedulingBuilder.UseStorageBackedScheduler(options =>
        {
            options.PollingInterval = TimeSpan.FromSeconds(5);
            options.BatchSize = 50;
            options.MaxConcurrency = 20;
            options.AutoCleanup = false;
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetService<StorageBackedSchedulerOptions>();
        Assert.NotNull(options);
        Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
        Assert.Equal(50, options.BatchSize);
        Assert.Equal(20, options.MaxConcurrency);
        Assert.False(options.AutoCleanup);
    }

    [Fact]
    public void UseStorageBackedScheduler_WithNullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        ISchedulingBuilder? builder = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder!.UseStorageBackedScheduler());
    }

    [Fact]
    public void StorageBackedSchedulerOptions_DefaultValues_AreCorrect()
    {
        // Act
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
    public void StorageBackedSchedulerOptions_CanSetCustomValues()
    {
        // Act
        var options = new StorageBackedSchedulerOptions
        {
            PollingInterval = TimeSpan.FromSeconds(10),
            BatchSize = 200,
            MaxConcurrency = 5,
            AutoCleanup = false,
            CleanupAge = TimeSpan.FromDays(7),
            CleanupInterval = TimeSpan.FromHours(12)
        };

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(10), options.PollingInterval);
        Assert.Equal(200, options.BatchSize);
        Assert.Equal(5, options.MaxConcurrency);
        Assert.False(options.AutoCleanup);
        Assert.Equal(TimeSpan.FromDays(7), options.CleanupAge);
        Assert.Equal(TimeSpan.FromHours(12), options.CleanupInterval);
    }

    // Test helper class
    private sealed class SchedulingBuilderMock : ISchedulingBuilder
    {
        public SchedulingBuilderMock(IServiceCollection services)
        {
            Services = services;
        }

        public IServiceCollection Services { get; }
    }
}
