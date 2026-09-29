using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Events;
using HeroMessaging.Configuration;
using HeroMessaging.Processing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Unit.Processing;

[Trait("Category", "Unit")]
public sealed class EventBusSettingsTests
{
    [Theory]
    [InlineData(1, 1, 128, 8)]
    [InlineData(4, 4, 512, 32)]
    [InlineData(8, 8, 1000, 64)]
    [InlineData(64, 32, 1000, 64)]
    [InlineData(int.MaxValue, 32, 1000, 64)]
    public void Defaults_AreBoundedByEnvironment(int processors, int parallelism, int capacity, int poolSize)
    {
        var settings = EventBusSettings.Resolve(new EventBusOptions(), processors);

        Assert.Equal(parallelism, settings.MaxDegreeOfParallelism);
        Assert.Equal(capacity, settings.BoundedCapacity);
        Assert.Equal(poolSize, settings.MaxPooledEnvelopes);
    }

    [Fact]
    public void ExplicitValues_OverrideDefaultCaps()
    {
        var options = new EventBusOptions
        {
            MaxDegreeOfParallelism = 48,
            BoundedCapacity = 2000,
            MaxPooledEnvelopes = 128
        };

        var settings = EventBusSettings.Resolve(options, 2);

        Assert.Equal(48, settings.MaxDegreeOfParallelism);
        Assert.Equal(2000, settings.BoundedCapacity);
        Assert.Equal(128, settings.MaxPooledEnvelopes);
    }

    [Fact]
    public void Pooling_CanBeDisabled()
    {
        var settings = EventBusSettings.Resolve(new EventBusOptions { MaxPooledEnvelopes = 0 }, 4);

        Assert.Equal(0, settings.MaxPooledEnvelopes);
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(null, 0, null)]
    [InlineData(null, null, -1)]
    public void InvalidOverrides_FailAtConstruction(int? parallelism, int? capacity, int? poolSize)
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var options = new EventBusOptions
        {
            MaxDegreeOfParallelism = parallelism,
            BoundedCapacity = capacity,
            MaxPooledEnvelopes = poolSize
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new EventBus(provider, null, options));
    }

    [Fact]
    public async Task Builder_RegistersConfiguredOptionsForEventBus()
    {
        var services = new ServiceCollection();
        new HeroMessagingBuilder(services)
            .WithEventBus(options =>
            {
                options.MaxDegreeOfParallelism = 2;
                options.BoundedCapacity = 3;
                options.MaxPooledEnvelopes = 4;
            })
            .Build();

        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<EventBusOptions>();
        var bus = provider.GetRequiredService<IEventBus>();

        Assert.Equal(2, options.MaxDegreeOfParallelism);
        Assert.Equal(3, options.BoundedCapacity);
        Assert.Equal(4, options.MaxPooledEnvelopes);
        Assert.IsType<EventBus>(bus);
    }

    [Fact]
    public void Builder_AppliesOptionsWhenResolvingEventBus()
    {
        var services = new ServiceCollection();
        new HeroMessagingBuilder(services)
            .WithEventBus(options => options.BoundedCapacity = 0)
            .Build();

        using var provider = services.BuildServiceProvider();

        Assert.Throws<ArgumentOutOfRangeException>(provider.GetRequiredService<IEventBus>);
    }
}
