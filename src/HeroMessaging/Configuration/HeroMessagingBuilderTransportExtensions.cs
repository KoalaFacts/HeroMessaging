using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Configuration;

/// <summary>
/// Extension methods for configuring transport layer options in HeroMessaging
/// </summary>
public static class HeroMessagingBuilderTransportExtensions
{
    /// <summary>
    /// Configure in-memory queue options
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="configure">Configuration action for queue options</param>
    /// <returns>The builder for chaining</returns>
    public static IHeroMessagingBuilder ConfigureInMemoryQueue(
        this IHeroMessagingBuilder builder,
        Action<InMemoryQueueOptions> configure)
    {
        var options = new InMemoryQueueOptions();
        configure(options);
        options.Validate();

        builder.Services.AddSingleton(options);
        return builder;
    }

    /// <summary>
    /// Use Channel-based queue mode (default, async/await based)
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="bufferSize">Maximum queue buffer size (default: 1024)</param>
    /// <param name="dropWhenFull">Drop oldest messages when buffer is full (default: false)</param>
    /// <returns>The builder for chaining</returns>
    public static IHeroMessagingBuilder UseChannelQueue(
        this IHeroMessagingBuilder builder,
        int bufferSize = 1024,
        bool dropWhenFull = false)
    {
        return builder.ConfigureInMemoryQueue(options =>
        {
            options.Mode = QueueMode.Channel;
            options.BufferSize = bufferSize;
            options.DropWhenFull = dropWhenFull;
        });
    }

    /// <summary>
    /// Use RingBuffer-based queue mode (lock-free, zero-allocation, ultra-low latency)
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="bufferSize">Ring buffer size (must be power of 2, default: 1024)</param>
    /// <param name="waitStrategy">Wait strategy for consumers (default: Sleeping)</param>
    /// <param name="producerMode">Producer coordination mode (default: Multi)</param>
    /// <returns>The builder for chaining</returns>
    /// <exception cref="ArgumentException">Thrown if bufferSize is not a power of 2</exception>
    public static IHeroMessagingBuilder UseRingBufferQueue(
        this IHeroMessagingBuilder builder,
        int bufferSize = 1024,
        WaitStrategy waitStrategy = WaitStrategy.Sleeping,
        ProducerMode producerMode = ProducerMode.Multi)
    {
        return builder.ConfigureInMemoryQueue(options =>
        {
            options.Mode = QueueMode.RingBuffer;
            options.BufferSize = bufferSize;
            options.WaitStrategy = waitStrategy;
            options.ProducerMode = producerMode;
        });
    }

    /// <summary>
    /// Use RingBuffer with ultra-low latency settings (BusySpin wait strategy, single producer)
    /// WARNING: Uses 100% of a CPU core. Only use when ultra-low latency is critical.
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="bufferSize">Ring buffer size (must be power of 2, default: 2048)</param>
    /// <returns>The builder for chaining</returns>
    public static IHeroMessagingBuilder UseRingBufferUltraLowLatency(
        this IHeroMessagingBuilder builder,
        int bufferSize = 2048)
    {
        return builder.UseRingBufferQueue(
            bufferSize: bufferSize,
            waitStrategy: WaitStrategy.BusySpin,
            producerMode: ProducerMode.Single);
    }

    /// <summary>
    /// Use RingBuffer with balanced settings for general-purpose high performance
    /// </summary>
    /// <param name="builder">The HeroMessaging builder</param>
    /// <param name="bufferSize">Ring buffer size (must be power of 2, default: 1024)</param>
    /// <returns>The builder for chaining</returns>
    public static IHeroMessagingBuilder UseRingBufferBalanced(
        this IHeroMessagingBuilder builder,
        int bufferSize = 1024)
    {
        return builder.UseRingBufferQueue(
            bufferSize: bufferSize,
            waitStrategy: WaitStrategy.Sleeping,
            producerMode: ProducerMode.Multi);
    }
}
