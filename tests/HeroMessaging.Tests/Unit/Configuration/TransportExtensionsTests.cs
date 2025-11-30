using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Transport;
using HeroMessaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HeroMessaging.Tests.Unit.Configuration;

/// <summary>
/// Unit tests for HeroMessagingBuilderTransportExtensions.
/// </summary>
[Trait("Category", "Unit")]
public class TransportExtensionsTests
{
    private readonly ServiceCollection _services;
    private readonly HeroMessagingBuilder _builder;

    public TransportExtensionsTests()
    {
        _services = new ServiceCollection();
        _builder = new HeroMessagingBuilder(_services);
    }

    #region ConfigureInMemoryQueue Tests

    [Fact]
    public void ConfigureInMemoryQueue_WithValidOptions_RegistersOptions()
    {
        // Act
        _builder.ConfigureInMemoryQueue(options =>
        {
            options.Mode = QueueMode.Channel;
            options.BufferSize = 2048;
        });
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(QueueMode.Channel, options.Mode);
        Assert.Equal(2048, options.BufferSize);
    }

    [Fact]
    public void ConfigureInMemoryQueue_ReturnsSameBuilder_ForChaining()
    {
        // Act
        var result = _builder.ConfigureInMemoryQueue(options => { });

        // Assert
        Assert.Same(_builder, result);
    }

    [Fact]
    public void ConfigureInMemoryQueue_WithInvalidBufferSize_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.ConfigureInMemoryQueue(options =>
            {
                options.BufferSize = 0;
            }));
        Assert.Contains("BufferSize must be positive", exception.Message);
    }

    [Fact]
    public void ConfigureInMemoryQueue_RingBufferMode_NonPowerOfTwo_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.ConfigureInMemoryQueue(options =>
            {
                options.Mode = QueueMode.RingBuffer;
                options.BufferSize = 100;  // Not power of 2
            }));
        Assert.Contains("BufferSize must be power of 2", exception.Message);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(256)]
    [InlineData(1024)]
    [InlineData(4096)]
    public void ConfigureInMemoryQueue_RingBufferMode_PowerOfTwo_Succeeds(int bufferSize)
    {
        // Act - Should not throw
        _builder.ConfigureInMemoryQueue(options =>
        {
            options.Mode = QueueMode.RingBuffer;
            options.BufferSize = bufferSize;
        });
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(QueueMode.RingBuffer, options.Mode);
        Assert.Equal(bufferSize, options.BufferSize);
    }

    #endregion

    #region UseChannelQueue Tests

    [Fact]
    public void UseChannelQueue_WithDefaultParameters_UsesDefaults()
    {
        // Act
        _builder.UseChannelQueue();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(QueueMode.Channel, options.Mode);
        Assert.Equal(1024, options.BufferSize);
        Assert.False(options.DropWhenFull);
    }

    [Fact]
    public void UseChannelQueue_WithCustomBufferSize_SetsBufferSize()
    {
        // Act
        _builder.UseChannelQueue(bufferSize: 4096);
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(4096, options.BufferSize);
    }

    [Fact]
    public void UseChannelQueue_WithDropWhenFull_SetsDropWhenFull()
    {
        // Act
        _builder.UseChannelQueue(dropWhenFull: true);
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.True(options.DropWhenFull);
    }

    [Fact]
    public void UseChannelQueue_WithAllCustomParameters_SetsAllValues()
    {
        // Act
        _builder.UseChannelQueue(bufferSize: 2048, dropWhenFull: true);
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(QueueMode.Channel, options.Mode);
        Assert.Equal(2048, options.BufferSize);
        Assert.True(options.DropWhenFull);
    }

    [Fact]
    public void UseChannelQueue_ReturnsSameBuilder_ForChaining()
    {
        // Act
        var result = _builder.UseChannelQueue();

        // Assert
        Assert.Same(_builder, result);
    }

    #endregion

    #region UseRingBufferQueue Tests

    [Fact]
    public void UseRingBufferQueue_WithDefaultParameters_UsesDefaults()
    {
        // Act
        _builder.UseRingBufferQueue();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(QueueMode.RingBuffer, options.Mode);
        Assert.Equal(1024, options.BufferSize);
        Assert.Equal(WaitStrategy.Sleeping, options.WaitStrategy);
        Assert.Equal(ProducerMode.Multi, options.ProducerMode);
    }

    [Fact]
    public void UseRingBufferQueue_WithCustomBufferSize_SetsBufferSize()
    {
        // Act
        _builder.UseRingBufferQueue(bufferSize: 2048);
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(2048, options.BufferSize);
    }

    [Fact]
    public void UseRingBufferQueue_WithCustomWaitStrategy_SetsWaitStrategy()
    {
        // Act
        _builder.UseRingBufferQueue(waitStrategy: WaitStrategy.Yielding);
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(WaitStrategy.Yielding, options.WaitStrategy);
    }

    [Fact]
    public void UseRingBufferQueue_WithCustomProducerMode_SetsProducerMode()
    {
        // Act
        _builder.UseRingBufferQueue(producerMode: ProducerMode.Single);
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(ProducerMode.Single, options.ProducerMode);
    }

    [Fact]
    public void UseRingBufferQueue_WithAllCustomParameters_SetsAllValues()
    {
        // Act
        _builder.UseRingBufferQueue(
            bufferSize: 4096,
            waitStrategy: WaitStrategy.BusySpin,
            producerMode: ProducerMode.Single);
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(QueueMode.RingBuffer, options.Mode);
        Assert.Equal(4096, options.BufferSize);
        Assert.Equal(WaitStrategy.BusySpin, options.WaitStrategy);
        Assert.Equal(ProducerMode.Single, options.ProducerMode);
    }

    [Fact]
    public void UseRingBufferQueue_WithNonPowerOfTwoBufferSize_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            _builder.UseRingBufferQueue(bufferSize: 1000));
        Assert.Contains("BufferSize must be power of 2", exception.Message);
    }

    [Fact]
    public void UseRingBufferQueue_ReturnsSameBuilder_ForChaining()
    {
        // Act
        var result = _builder.UseRingBufferQueue();

        // Assert
        Assert.Same(_builder, result);
    }

    [Theory]
    [InlineData(WaitStrategy.Blocking)]
    [InlineData(WaitStrategy.Sleeping)]
    [InlineData(WaitStrategy.Yielding)]
    [InlineData(WaitStrategy.BusySpin)]
    [InlineData(WaitStrategy.TimeoutBlocking)]
    public void UseRingBufferQueue_WithAllWaitStrategies_Succeeds(WaitStrategy strategy)
    {
        // Act
        _builder.UseRingBufferQueue(waitStrategy: strategy);
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(strategy, options.WaitStrategy);
    }

    #endregion

    #region UseRingBufferUltraLowLatency Tests

    [Fact]
    public void UseRingBufferUltraLowLatency_WithDefaultParameters_UsesUltraLowLatencySettings()
    {
        // Act
        _builder.UseRingBufferUltraLowLatency();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(QueueMode.RingBuffer, options.Mode);
        Assert.Equal(2048, options.BufferSize);
        Assert.Equal(WaitStrategy.BusySpin, options.WaitStrategy);
        Assert.Equal(ProducerMode.Single, options.ProducerMode);
    }

    [Fact]
    public void UseRingBufferUltraLowLatency_WithCustomBufferSize_SetsBufferSize()
    {
        // Act
        _builder.UseRingBufferUltraLowLatency(bufferSize: 8192);
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(8192, options.BufferSize);
        Assert.Equal(WaitStrategy.BusySpin, options.WaitStrategy);
        Assert.Equal(ProducerMode.Single, options.ProducerMode);
    }

    [Fact]
    public void UseRingBufferUltraLowLatency_ReturnsSameBuilder_ForChaining()
    {
        // Act
        var result = _builder.UseRingBufferUltraLowLatency();

        // Assert
        Assert.Same(_builder, result);
    }

    #endregion

    #region UseRingBufferBalanced Tests

    [Fact]
    public void UseRingBufferBalanced_WithDefaultParameters_UsesBalancedSettings()
    {
        // Act
        _builder.UseRingBufferBalanced();
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(QueueMode.RingBuffer, options.Mode);
        Assert.Equal(1024, options.BufferSize);
        Assert.Equal(WaitStrategy.Sleeping, options.WaitStrategy);
        Assert.Equal(ProducerMode.Multi, options.ProducerMode);
    }

    [Fact]
    public void UseRingBufferBalanced_WithCustomBufferSize_SetsBufferSize()
    {
        // Act
        _builder.UseRingBufferBalanced(bufferSize: 4096);
        var provider = _services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<InMemoryQueueOptions>();
        Assert.Equal(4096, options.BufferSize);
        Assert.Equal(WaitStrategy.Sleeping, options.WaitStrategy);
        Assert.Equal(ProducerMode.Multi, options.ProducerMode);
    }

    [Fact]
    public void UseRingBufferBalanced_ReturnsSameBuilder_ForChaining()
    {
        // Act
        var result = _builder.UseRingBufferBalanced();

        // Assert
        Assert.Same(_builder, result);
    }

    #endregion

    #region InMemoryQueueOptions Tests

    [Fact]
    public void InMemoryQueueOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new InMemoryQueueOptions();

        // Assert
        Assert.Equal(QueueMode.Channel, options.Mode);
        Assert.Equal(1024, options.BufferSize);
        Assert.False(options.DropWhenFull);
        Assert.Equal(WaitStrategy.Sleeping, options.WaitStrategy);
        Assert.Equal(ProducerMode.Multi, options.ProducerMode);
    }

    [Fact]
    public void InMemoryQueueOptions_Validate_WithValidChannelOptions_DoesNotThrow()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.Channel,
            BufferSize = 100  // Any positive value is fine for Channel mode
        };

        // Act & Assert - Should not throw
        options.Validate();
    }

    [Fact]
    public void InMemoryQueueOptions_Validate_WithNegativeBufferSize_ThrowsArgumentException()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            BufferSize = -1
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Contains("BufferSize must be positive", exception.Message);
    }

    [Fact]
    public void InMemoryQueueOptions_Validate_WithZeroBufferSize_ThrowsArgumentException()
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            BufferSize = 0
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Contains("BufferSize must be positive", exception.Message);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(1023)]
    [InlineData(1025)]
    public void InMemoryQueueOptions_Validate_RingBufferMode_NonPowerOfTwo_ThrowsArgumentException(int bufferSize)
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = bufferSize
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(options.Validate);
        Assert.Contains("BufferSize must be power of 2", exception.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    public void InMemoryQueueOptions_Validate_RingBufferMode_PowerOfTwo_DoesNotThrow(int bufferSize)
    {
        // Arrange
        var options = new InMemoryQueueOptions
        {
            Mode = QueueMode.RingBuffer,
            BufferSize = bufferSize
        };

        // Act & Assert - Should not throw
        options.Validate();
    }

    #endregion
}
