using System.Collections.Concurrent;
using System.Diagnostics;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Processing.Decorators;

/// <summary>
/// Decorator that enables batch processing of messages for improved throughput
/// </summary>
/// <remarks>
/// <para>
/// This decorator accumulates messages and processes them in batches to improve throughput
/// while maintaining the full processing pipeline for each individual message.
/// </para>
/// <para>
/// <strong>Design Principles</strong>:
/// </para>
/// <list type="bullet">
/// <item><description>Each message maintains full decorator chain (validation, retry, circuit breaker, etc.)</description></item>
/// <item><description>Thread-safe accumulation with configurable batch size and timeout</description></item>
/// <item><description>Performance target: 20-40% throughput improvement for batch-friendly workloads</description></item>
/// <item><description>Graceful fallback to individual processing on batch failures</description></item>
/// </list>
/// <para>
/// <strong>Pipeline Position</strong>: Should be positioned early in the pipeline, typically:
/// </para>
/// <list type="number">
/// <item><description>ValidationDecorator - Validate before batching</description></item>
/// <item><description>BatchDecorator - Accumulate and batch (this)</description></item>
/// <item><description>IdempotencyDecorator - Check cache per message</description></item>
/// <item><description>RetryDecorator - Retry per message</description></item>
/// <item><description>Handler Execution</description></item>
/// </list>
/// <para>
/// <strong>Important</strong>: Use <see cref="CreateAsync"/> factory method to create instances.
/// This ensures proper async initialization and avoids race conditions with FakeTimeProvider in tests.
/// </para>
/// </remarks>
public sealed class BatchDecorator : MessageProcessorDecorator, IAsyncDisposable
{
    private readonly BatchProcessingOptions _options;
    private readonly ILogger<BatchDecorator> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly ConcurrentQueue<BatchItem> _messageQueue = new();
    private readonly SemaphoreSlim _batchSignal = new(0);
    private readonly CancellationTokenSource _disposalCts = new();
    private Task _batchProcessingTask = Task.CompletedTask;

    private int _queuedCount;
    private long _totalProcessed;
    private long _totalBatches;

    // Test hook: signaled after each batch processing loop iteration completes
    // Using SemaphoreSlim instead of TaskCompletionSource to properly handle producer-consumer pattern
    // Each Release() increments count, each WaitAsync() decrements - no race condition
    private readonly SemaphoreSlim _batchIterationCompleted = new(0);

    // Test hook: signaled when the background loop is about to enter its wait state
    // This allows tests to know when it's safe to advance FakeTimeProvider
    // Using SemaphoreSlim for same reason - handles producer-consumer properly
    private readonly SemaphoreSlim _loopReadyToWait = new(0);

    // Signaled when the background loop has started and created its first delay
    // Used by CreateAsync to ensure proper initialization before returning
    // Uses TaskCompletionSource<bool> for netstandard2.0 compatibility (non-generic TCS not available)
    private readonly TaskCompletionSource<bool> _loopInitialized = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Creates a new BatchDecorator with proper async initialization.
    /// This is the recommended way to create a BatchDecorator as it ensures the background
    /// loop is properly initialized before returning, avoiding race conditions with FakeTimeProvider.
    /// </summary>
    /// <param name="inner">The inner message processor to decorate.</param>
    /// <param name="options">The batch processing configuration options.</param>
    /// <param name="logger">The logger for diagnostic information.</param>
    /// <param name="timeProvider">The time provider for timestamp management.</param>
    /// <param name="cancellationToken">Cancellation token for the initialization.</param>
    /// <returns>A fully initialized BatchDecorator instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any required parameter is null.</exception>
    /// <exception cref="ArgumentException">Thrown when options validation fails.</exception>
    public static async Task<BatchDecorator> CreateAsync(
        IMessageProcessor inner,
        BatchProcessingOptions options,
        ILogger<BatchDecorator> logger,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        var decorator = new BatchDecorator(inner, options, logger, timeProvider);

        if (options.Enabled)
        {
            decorator.StartBackgroundLoop();

            // Wait for the background loop to reach its first wait state
            // This ensures the delay is created before the caller can advance FakeTimeProvider
#if NET8_0_OR_GREATER
            await decorator._loopInitialized.Task.WaitAsync(cancellationToken);
#else
            // For netstandard2.0: race with cancellation task
            var cancellationTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancellationTcs.TrySetCanceled(cancellationToken)))
            {
                await Task.WhenAny(decorator._loopInitialized.Task, cancellationTcs.Task);
                cancellationToken.ThrowIfCancellationRequested();
            }
#endif
        }

        return decorator;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchDecorator"/> class.
    /// </summary>
    /// <param name="inner">The inner message processor to decorate.</param>
    /// <param name="options">The batch processing configuration options.</param>
    /// <param name="logger">The logger for diagnostic information.</param>
    /// <param name="timeProvider">The time provider for timestamp management.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    /// <exception cref="ArgumentException">Thrown when options validation fails.</exception>
    /// <remarks>
    /// Consider using <see cref="CreateAsync"/> factory method for proper async initialization,
    /// especially when using FakeTimeProvider in tests. The constructor does not start the
    /// background loop automatically - call <see cref="StartBackgroundLoop"/> after construction
    /// if using this constructor directly.
    /// </remarks>
    public BatchDecorator(
        IMessageProcessor inner,
        BatchProcessingOptions options,
        ILogger<BatchDecorator> logger,
        TimeProvider? timeProvider = null)
        : base(inner)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;

        _options.Validate();

        if (!_options.Enabled)
        {
            // Mark as initialized immediately for disabled mode
            _loopInitialized.TrySetResult(true);
            _logger.LogDebug("BatchDecorator initialized in disabled mode - messages will be processed immediately");
        }
    }

    /// <summary>
    /// Starts the background processing loop. Call this after construction if not using CreateAsync.
    /// This method is idempotent - calling it multiple times has no effect after the first call.
    /// </summary>
    public void StartBackgroundLoop()
    {
        if (!_options.Enabled || _batchProcessingTask != Task.CompletedTask)
            return;

        _batchProcessingTask = Task.Run(BatchProcessingLoopAsync, _disposalCts.Token);

        _logger.LogInformation(
            "BatchDecorator started with MaxBatchSize={MaxBatchSize}, BatchTimeout={BatchTimeout}ms, MinBatchSize={MinBatchSize}",
            _options.MaxBatchSize,
            _options.BatchTimeout.TotalMilliseconds,
            _options.MinBatchSize);
    }

    /// <summary>
    /// Waits for the background loop to be ready to wait (about to enter its delay).
    /// Call this BEFORE advancing FakeTimeProvider to ensure the timer exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is critical for deterministic testing with FakeTimeProvider:
    /// 1. Call this method to ensure the loop is ready
    /// 2. Then advance FakeTimeProvider time
    /// 3. Then call WaitForBatchIterationAsync to wait for processing to complete
    /// </para>
    /// <para>
    /// This method waits indefinitely for the signal (only cancellation token can abort).
    /// This is intentional - no real-time timeout is used because it would be incompatible
    /// with FakeTimeProvider-based testing. The background loop will signal when ready.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token to abort the wait.</param>
    /// <returns>A task that completes when the loop is ready to wait.</returns>
    public async Task WaitForLoopReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        // Uses SemaphoreSlim for proper producer-consumer pattern
        // Signal is released when loop enters wait state, consumed here
        await _loopReadyToWait.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Waits for the next batch processing loop iteration to complete.
    /// This is a test hook that enables deterministic testing with FakeTimeProvider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Instead of using timing-based delays in tests, call this method after advancing
    /// FakeTimeProvider to wait for the background loop to actually process the batch.
    /// </para>
    /// <para>
    /// This method waits indefinitely for the signal (only cancellation token can abort).
    /// This is intentional - no real-time timeout is used because it would be incompatible
    /// with FakeTimeProvider-based testing. The background loop will signal after each iteration.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token to abort the wait.</param>
    /// <returns>A task that completes when the next batch iteration finishes.</returns>
    public async Task WaitForBatchIterationAsync(CancellationToken cancellationToken = default)
    {
        // Uses SemaphoreSlim for proper producer-consumer pattern
        // Signal is released when iteration completes, consumed here
        // This avoids the race condition with TaskCompletionSource where the TCS
        // could be reset before the waiter gets a chance to observe it
        await _batchIterationCompleted.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask<ProcessingResult> ProcessAsync(
        IMessage message,
        ProcessingContext context,
        CancellationToken cancellationToken = default)
    {
        // If batching is disabled, process immediately
        if (!_options.Enabled)
        {
            return await _inner.ProcessAsync(message, context, cancellationToken);
        }

        // Create completion source for this message
        var tcs = new TaskCompletionSource<ProcessingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new BatchItem(message, context, tcs, cancellationToken);

        // Enqueue message
        _messageQueue.Enqueue(item);
        var count = Interlocked.Increment(ref _queuedCount);

        _logger.LogTrace(
            "Message {MessageId} queued for batch processing (queue size: {QueueSize})",
            message.MessageId,
            count);

        // If we've reached max batch size, signal immediate processing
        if (count >= _options.MaxBatchSize)
        {
            _batchSignal.Release();
        }

        // Wait for result
        return await tcs.Task;
    }

    /// <summary>
    /// Background loop that processes batches based on size and timeout
    /// Uses TimeProvider for async waiting to enable deterministic testing
    /// </summary>
    private async Task BatchProcessingLoopAsync()
    {
        _logger.LogDebug("Batch processing loop started");

        try
        {
            while (!_disposalCts.Token.IsCancellationRequested)
            {
                // Wait for either batch signal or timeout using TimeProvider
                // This enables deterministic testing with FakeTimeProvider
                // Note: Using SemaphoreSlim for signals eliminates the need for "Prepare*" calls
                await WaitForBatchOrTimeoutAsync(_disposalCts.Token);

                var queuedCount = Interlocked.Exchange(ref _queuedCount, 0);

                if (queuedCount == 0)
                {
                    // Signal completion even for empty iterations so tests can synchronize
                    SignalBatchIterationCompleted();
                    continue; // Timeout with no messages
                }

                // Dequeue messages
                var batch = new List<BatchItem>(Math.Min(queuedCount, _options.MaxBatchSize));
                for (var i = 0; i < queuedCount && i < _options.MaxBatchSize; i++)
                {
                    if (_messageQueue.TryDequeue(out var item))
                    {
                        batch.Add(item);
                    }
                }

                if (batch.Count == 0)
                {
                    SignalBatchIterationCompleted();
                    continue;
                }

                // Process batch
                await ProcessBatchInternalAsync(batch);

                // Signal that this batch iteration is complete (for test synchronization)
                SignalBatchIterationCompleted();
            }
        }
        catch (OperationCanceledException) when (_disposalCts.Token.IsCancellationRequested)
        {
            _logger.LogDebug("Batch processing loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in batch processing loop");
        }

        _logger.LogDebug("Batch processing loop stopped");
    }

    /// <summary>
    /// Waits for either the batch signal or timeout using TimeProvider for testability
    /// </summary>
    private async Task WaitForBatchOrTimeoutAsync(CancellationToken cancellationToken)
    {
        // Early exit if already cancelled
        cancellationToken.ThrowIfCancellationRequested();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Create timeout task using TimeProvider (enables FakeTimeProvider testing)
#if NET8_0_OR_GREATER
        var delayTask = Task.Delay(_options.BatchTimeout, _timeProvider, timeoutCts.Token);
#else
        var delayTask = _timeProvider.Delay(_options.BatchTimeout, timeoutCts.Token);
#endif

        // Create signal wait task
        var signalTask = _batchSignal.WaitAsync(timeoutCts.Token);

        // Create cancellation task that completes when original token is cancelled
        // This ensures we exit promptly on cancellation even with FakeTimeProvider
        var cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);

        // Signal that we're about to wait - this allows tests to know when it's safe to advance FakeTimeProvider
        SignalLoopReady();

        // CRITICAL: Signal loop initialized RIGHT BEFORE await. This is what CreateAsync awaits.
        // Must happen after all setup and right before await to ensure we're actually ready for time advances.
        _loopInitialized.TrySetResult(true);

        // Wait for whichever completes first (including cancellation)
        await Task.WhenAny(signalTask, delayTask, cancellationTask);

        // Cancel the other tasks (use Cancel() for netstandard2.0 compatibility)
        timeoutCts.Cancel();

        // Propagate cancellation - Task.WhenAny doesn't throw when inner tasks are cancelled
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Signals that the loop is ready to wait, for test synchronization with FakeTimeProvider.
    /// Uses SemaphoreSlim.Release() which properly handles the producer-consumer pattern.
    /// </summary>
    private void SignalLoopReady()
    {
        _loopReadyToWait.Release();
    }

    /// <summary>
    /// Signals that a batch processing iteration has completed, for test synchronization.
    /// Uses SemaphoreSlim.Release() which properly handles the producer-consumer pattern.
    /// </summary>
    private void SignalBatchIterationCompleted()
    {
        _batchIterationCompleted.Release();
    }

    /// <summary>
    /// Processes a batch of messages
    /// </summary>
    private async Task ProcessBatchInternalAsync(List<BatchItem> batch)
    {
        var batchSize = batch.Count;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug(
            "Processing batch of {BatchSize} messages (MinBatchSize={MinBatchSize})",
            batchSize,
            _options.MinBatchSize);

        // If batch is too small, process individually
        if (batchSize < _options.MinBatchSize)
        {
            _logger.LogTrace("Batch size {BatchSize} below minimum {MinBatchSize}, processing individually",
                batchSize, _options.MinBatchSize);

            await ProcessIndividuallyAsync(batch);
            return;
        }

        try
        {
            // Process messages based on parallelism setting
            if (_options.MaxDegreeOfParallelism == 1)
            {
                // Sequential processing
                var stopIndex = -1;
                for (var i = 0; i < batch.Count; i++)
                {
                    var item = batch[i];
                    await ProcessSingleMessageAsync(item);

                    if (!_options.ContinueOnFailure && item.CompletionSource.Task.IsCompleted)
                    {
                        var result = await item.CompletionSource.Task;
                        if (!result.Success)
                        {
                            _logger.LogWarning(
                                "Batch processing stopped at message {MessageId} due to failure",
                                item.Message.MessageId);
                            stopIndex = i;
                            break;
                        }
                    }
                }

                // If processing was stopped early, fail remaining items in the batch
                if (stopIndex >= 0)
                {
                    for (var i = stopIndex + 1; i < batch.Count; i++)
                    {
                        var item = batch[i];
                        if (!item.CompletionSource.Task.IsCompleted)
                        {
                            item.CompletionSource.TrySetResult(ProcessingResult.Failed(
                                new OperationCanceledException("Batch processing stopped due to earlier failure"),
                                "Batch processing stopped due to ContinueOnFailure=false"));
                        }
                    }
                }
            }
            else
            {
                // Parallel processing
                var tasks = new List<Task>();
                var semaphore = new SemaphoreSlim(_options.MaxDegreeOfParallelism);

                foreach (var item in batch)
                {
                    await semaphore.WaitAsync(_disposalCts.Token);

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessSingleMessageAsync(item);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, _disposalCts.Token);

                    tasks.Add(task);
                }

                await Task.WhenAll(tasks);
            }

            stopwatch.Stop();

            var successCount = 0;
            var failureCount = 0;

            foreach (var item in batch)
            {
                if (item.CompletionSource.Task.IsCompleted)
                {
                    var result = await item.CompletionSource.Task;
                    if (result.Success) successCount++;
                    else failureCount++;
                }
            }

            Interlocked.Add(ref _totalProcessed, batchSize);
            Interlocked.Increment(ref _totalBatches);

            _logger.LogInformation(
                "Batch processing completed: {BatchSize} messages in {ElapsedMs}ms ({SuccessCount} succeeded, {FailureCount} failed)",
                batchSize,
                stopwatch.ElapsedMilliseconds,
                successCount,
                failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch processing failed, falling back to individual processing");

            if (_options.FallbackToIndividualProcessing)
            {
                await ProcessIndividuallyAsync(batch);
            }
            else
            {
                // Fail all messages in batch
                foreach (var item in batch)
                {
                    if (!item.CompletionSource.Task.IsCompleted)
                    {
                        item.CompletionSource.TrySetResult(
                            ProcessingResult.Failed(ex, "Batch processing failed"));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Processes messages individually (fallback mode)
    /// </summary>
    private async Task ProcessIndividuallyAsync(List<BatchItem> batch)
    {
        _logger.LogDebug("Processing {Count} messages individually", batch.Count);

        foreach (var item in batch)
        {
            await ProcessSingleMessageAsync(item);
        }
    }

    /// <summary>
    /// Processes a single message and sets the completion result
    /// </summary>
    private async Task ProcessSingleMessageAsync(BatchItem item)
    {
        try
        {
            // Skip if already cancelled
            if (item.CancellationToken.IsCancellationRequested)
            {
                item.CompletionSource.TrySetCanceled(item.CancellationToken);
                return;
            }

            // Process through inner pipeline
            var result = await _inner.ProcessAsync(item.Message, item.Context, item.CancellationToken);

            item.CompletionSource.TrySetResult(result);
        }
        catch (OperationCanceledException) when (item.CancellationToken.IsCancellationRequested)
        {
            item.CompletionSource.TrySetCanceled(item.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}", item.Message.MessageId);
            item.CompletionSource.TrySetResult(ProcessingResult.Failed(ex));
        }
    }

    /// <summary>
    /// Disposes resources and flushes remaining messages
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation(
            "BatchDecorator disposing (Total processed: {TotalProcessed}, Total batches: {TotalBatches})",
            _totalProcessed,
            _totalBatches);

        // Stop accepting new messages
        _disposalCts.Cancel();

        // Wait for background task to complete
        try
        {
            await _batchProcessingTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Process remaining queued messages
        var remaining = new List<BatchItem>();
        while (_messageQueue.TryDequeue(out var item))
        {
            remaining.Add(item);
        }

        if (remaining.Count > 0)
        {
            _logger.LogInformation("Processing {Count} remaining messages before disposal", remaining.Count);
            await ProcessIndividuallyAsync(remaining);
        }

        _batchSignal.Dispose();
        _batchIterationCompleted.Dispose();
        _loopReadyToWait.Dispose();
        _disposalCts.Dispose();

        _logger.LogDebug("BatchDecorator disposed");
    }

    /// <summary>
    /// Internal item structure for batch queue
    /// </summary>
    private sealed record BatchItem(
        IMessage Message,
        ProcessingContext Context,
        TaskCompletionSource<ProcessingResult> CompletionSource,
        CancellationToken CancellationToken);
}
