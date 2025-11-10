using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Processing;
using HeroMessaging.Processing.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Configuration;

/// <summary>
/// Extension methods for configuring batch processing support in HeroMessaging.
/// </summary>
public static class ExtensionsToIHeroMessagingBuilderForBatchProcessing
{
    /// <summary>
    /// Adds batch processing support to the HeroMessaging pipeline.
    /// </summary>
    /// <param name="builder">The HeroMessaging builder.</param>
    /// <param name="configure">Optional action to configure batch processing settings.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// This method enables batch processing to improve throughput by accumulating messages
    /// and processing them together while maintaining the full processing pipeline for each message.
    /// </para>
    /// <para>
    /// <strong>Default Configuration</strong>:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Enabled: false (must be explicitly enabled)</description></item>
    /// <item><description>MaxBatchSize: 50</description></item>
    /// <item><description>BatchTimeout: 200ms</description></item>
    /// <item><description>MinBatchSize: 2</description></item>
    /// <item><description>MaxDegreeOfParallelism: 1 (sequential processing)</description></item>
    /// <item><description>ContinueOnFailure: true</description></item>
    /// <item><description>FallbackToIndividualProcessing: true</description></item>
    /// </list>
    /// <para>
    /// <strong>Pipeline Position</strong>: The batch decorator should be positioned early in the pipeline:
    /// </para>
    /// <list type="number">
    /// <item><description>ValidationDecorator - Validate before batching</description></item>
    /// <item><description>BatchDecorator - Accumulate and batch (this)</description></item>
    /// <item><description>IdempotencyDecorator - Check cache per message</description></item>
    /// <item><description>RetryDecorator - Retry per message</description></item>
    /// <item><description>Handler Execution</description></item>
    /// </list>
    /// <para>
    /// <strong>Performance Target</strong>: 20-40% throughput improvement for batch-friendly workloads
    /// </para>
    /// <para>
    /// <strong>Example Usage</strong>:
    /// </para>
    /// <code>
    /// services.AddHeroMessaging(builder =>
    /// {
    ///     builder.WithBatchProcessing(batch =>
    ///     {
    ///         batch
    ///             .Enable()
    ///             .WithMaxBatchSize(100)
    ///             .WithBatchTimeout(TimeSpan.FromMilliseconds(500))
    ///             .WithMinBatchSize(10)
    ///             .WithParallelProcessing(4);
    ///     });
    /// });
    /// </code>
    /// </remarks>
    public static IHeroMessagingBuilder WithBatchProcessing(
        this IHeroMessagingBuilder builder,
        Action<IBatchProcessingBuilder>? configure = null)
    {
        if (builder == null)
            throw new ArgumentNullException(nameof(builder));

        var services = builder.Build();
        var batchBuilder = new BatchProcessingBuilder(services);

        configure?.Invoke(batchBuilder);

        batchBuilder.Build();

        return builder;
    }
}

/// <summary>
/// Builder interface for configuring batch processing support.
/// </summary>
public interface IBatchProcessingBuilder
{
    /// <summary>
    /// Enables batch processing.
    /// </summary>
    /// <param name="enabled">True to enable batch processing, false to disable. Default is true.</param>
    /// <returns>The builder for method chaining.</returns>
    IBatchProcessingBuilder Enable(bool enabled = true);

    /// <summary>
    /// Configure the maximum number of messages to accumulate before processing a batch.
    /// </summary>
    /// <param name="maxBatchSize">
    /// The maximum batch size. Recommended: 10-100 for most workloads, 100-1000 for high-throughput scenarios.
    /// Default is 50.
    /// </param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when maxBatchSize is less than or equal to 0.</exception>
    IBatchProcessingBuilder WithMaxBatchSize(int maxBatchSize);

    /// <summary>
    /// Configure the maximum time to wait for messages before processing a partial batch.
    /// </summary>
    /// <param name="timeout">
    /// The batch timeout. Recommended: 100ms-1000ms depending on latency requirements.
    /// Default is 200ms.
    /// </param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when timeout is less than or equal to zero.</exception>
    IBatchProcessingBuilder WithBatchTimeout(TimeSpan timeout);

    /// <summary>
    /// Configure the minimum batch size before timeout-based processing is triggered.
    /// </summary>
    /// <param name="minBatchSize">
    /// The minimum batch size. Messages below this threshold are processed individually.
    /// Default is 2.
    /// </param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when minBatchSize is less than 1.</exception>
    IBatchProcessingBuilder WithMinBatchSize(int minBatchSize);

    /// <summary>
    /// Configure parallel processing of messages within a batch.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">
    /// The maximum degree of parallelism. Set to 1 for sequential processing (default),
    /// higher values for parallel processing. Note: Parallel processing may impact ordering guarantees.
    /// </param>
    /// <returns>The builder for method chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when maxDegreeOfParallelism is less than or equal to 0.</exception>
    IBatchProcessingBuilder WithParallelProcessing(int maxDegreeOfParallelism);

    /// <summary>
    /// Configure whether to continue processing remaining messages if some fail.
    /// </summary>
    /// <param name="continueOnFailure">
    /// True to continue processing on failure (default), false to stop at first failure.
    /// </param>
    /// <returns>The builder for method chaining.</returns>
    IBatchProcessingBuilder WithContinueOnFailure(bool continueOnFailure);

    /// <summary>
    /// Configure whether to fallback to individual processing if batch processing fails.
    /// </summary>
    /// <param name="fallback">
    /// True to fallback to individual processing (default), false to fail the entire batch.
    /// </param>
    /// <returns>The builder for method chaining.</returns>
    IBatchProcessingBuilder WithFallbackToIndividual(bool fallback);

    /// <summary>
    /// Configure batch processing with predefined settings for high-throughput scenarios.
    /// </summary>
    /// <returns>The builder for method chaining.</returns>
    /// <remarks>
    /// Settings: MaxBatchSize=100, BatchTimeout=500ms, MinBatchSize=10, Parallelism=4
    /// </remarks>
    IBatchProcessingBuilder UseHighThroughputProfile();

    /// <summary>
    /// Configure batch processing with predefined settings for low-latency scenarios.
    /// </summary>
    /// <returns>The builder for method chaining.</returns>
    /// <remarks>
    /// Settings: MaxBatchSize=20, BatchTimeout=100ms, MinBatchSize=5, Parallelism=1
    /// </remarks>
    IBatchProcessingBuilder UseLowLatencyProfile();

    /// <summary>
    /// Configure batch processing with predefined settings for balanced performance.
    /// </summary>
    /// <returns>The builder for method chaining.</returns>
    /// <remarks>
    /// Settings: MaxBatchSize=50, BatchTimeout=200ms, MinBatchSize=2, Parallelism=2
    /// </remarks>
    IBatchProcessingBuilder UseBalancedProfile();
}

/// <summary>
/// Implementation of the batch processing builder.
/// </summary>
internal sealed class BatchProcessingBuilder : IBatchProcessingBuilder
{
    private readonly IServiceCollection _services;
    private bool _enabled;
    private int _maxBatchSize = 50;
    private TimeSpan _batchTimeout = TimeSpan.FromMilliseconds(200);
    private int _minBatchSize = 2;
    private int _maxDegreeOfParallelism = 1;
    private bool _continueOnFailure = true;
    private bool _fallbackToIndividual = true;

    public BatchProcessingBuilder(IServiceCollection services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IBatchProcessingBuilder Enable(bool enabled = true)
    {
        _enabled = enabled;
        return this;
    }

    public IBatchProcessingBuilder WithMaxBatchSize(int maxBatchSize)
    {
        if (maxBatchSize <= 0)
            throw new ArgumentException("MaxBatchSize must be greater than 0.", nameof(maxBatchSize));

        _maxBatchSize = maxBatchSize;
        return this;
    }

    public IBatchProcessingBuilder WithBatchTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentException("BatchTimeout must be greater than zero.", nameof(timeout));

        _batchTimeout = timeout;
        return this;
    }

    public IBatchProcessingBuilder WithMinBatchSize(int minBatchSize)
    {
        if (minBatchSize < 1)
            throw new ArgumentException("MinBatchSize must be at least 1.", nameof(minBatchSize));

        _minBatchSize = minBatchSize;
        return this;
    }

    public IBatchProcessingBuilder WithParallelProcessing(int maxDegreeOfParallelism)
    {
        if (maxDegreeOfParallelism <= 0)
            throw new ArgumentException("MaxDegreeOfParallelism must be greater than 0.", nameof(maxDegreeOfParallelism));

        _maxDegreeOfParallelism = maxDegreeOfParallelism;
        return this;
    }

    public IBatchProcessingBuilder WithContinueOnFailure(bool continueOnFailure)
    {
        _continueOnFailure = continueOnFailure;
        return this;
    }

    public IBatchProcessingBuilder WithFallbackToIndividual(bool fallback)
    {
        _fallbackToIndividual = fallback;
        return this;
    }

    public IBatchProcessingBuilder UseHighThroughputProfile()
    {
        _enabled = true;
        _maxBatchSize = 100;
        _batchTimeout = TimeSpan.FromMilliseconds(500);
        _minBatchSize = 10;
        _maxDegreeOfParallelism = 4;
        return this;
    }

    public IBatchProcessingBuilder UseLowLatencyProfile()
    {
        _enabled = true;
        _maxBatchSize = 20;
        _batchTimeout = TimeSpan.FromMilliseconds(100);
        _minBatchSize = 5;
        _maxDegreeOfParallelism = 1;
        return this;
    }

    public IBatchProcessingBuilder UseBalancedProfile()
    {
        _enabled = true;
        _maxBatchSize = 50;
        _batchTimeout = TimeSpan.FromMilliseconds(200);
        _minBatchSize = 2;
        _maxDegreeOfParallelism = 2;
        return this;
    }

    internal void Build()
    {
        // Register TimeProvider if not already registered
        _services.TryAddSingleton(TimeProvider.System);

        // Register BatchProcessingOptions as singleton
        _services.TryAddSingleton(sp =>
        {
            var options = new BatchProcessingOptions
            {
                Enabled = _enabled,
                MaxBatchSize = _maxBatchSize,
                BatchTimeout = _batchTimeout,
                MinBatchSize = _minBatchSize,
                MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                ContinueOnFailure = _continueOnFailure,
                FallbackToIndividualProcessing = _fallbackToIndividual
            };

            options.Validate();
            return options;
        });

        // Register BatchDecorator factory
        // The decorator will be instantiated per message processor
        _services.TryAddSingleton<Func<IMessageProcessor, BatchDecorator>>(sp =>
        {
            var options = sp.GetRequiredService<BatchProcessingOptions>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;

            return inner =>
            {
                var logger = loggerFactory.CreateLogger<BatchDecorator>();
                return new BatchDecorator(inner, options, logger, timeProvider);
            };
        });
    }
}
