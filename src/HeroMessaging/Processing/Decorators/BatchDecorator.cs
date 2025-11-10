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
/// </remarks>
public sealed class BatchDecorator : MessageProcessorDecorator, IAsyncDisposable
{
    private readonly BatchProcessingOptions _options;
    private readonly ILogger<BatchDecorator> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly ConcurrentQueue<BatchItem> _messageQueue = new();
    private readonly SemaphoreSlim _batchSemaphore = new(1, 1);
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly Task _batchProcessingTask;

    private int _queuedCount;
    private long _totalProcessed;
    private long _totalBatches;

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchDecorator"/> class.
    /// </summary>
    /// <param name="inner">The inner message processor to decorate.</param>
    /// <param name="options">The batch processing configuration options.</param>
    /// <param name="logger">The logger for diagnostic information.</param>
    /// <param name="timeProvider">The time provider for timestamp management.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    /// <exception cref="ArgumentException">Thrown when options validation fails.</exception>
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

        // Start background batch processing task
        _batchProcessingTask = Task.Run(BatchProcessingLoopAsync, _disposalCts.Token);

        _logger.LogInformation(
            "BatchDecorator initialized with MaxBatchSize={MaxBatchSize}, BatchTimeout={BatchTimeout}ms, MinBatchSize={MinBatchSize}",
            _options.MaxBatchSize,
            _options.BatchTimeout.TotalMilliseconds,
            _options.MinBatchSize);
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

        // If we've reached max batch size, trigger immediate processing
        if (count >= _options.MaxBatchSize)
        {
            _batchSemaphore.Release();
        }

        // Wait for result
        return await tcs.Task;
    }

    /// <summary>
    /// Background loop that processes batches based on size and timeout
    /// </summary>
    private async Task BatchProcessingLoopAsync()
    {
        _logger.LogDebug("Batch processing loop started");

        try
        {
            while (!_disposalCts.Token.IsCancellationRequested)
            {
                // Wait for either batch to fill or timeout
                await _batchSemaphore.WaitAsync(_options.BatchTimeout, _disposalCts.Token);

                var queuedCount = Interlocked.Exchange(ref _queuedCount, 0);

                if (queuedCount == 0)
                {
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
                    continue;
                }

                // Process batch
                await ProcessBatchInternalAsync(batch);
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
                foreach (var item in batch)
                {
                    await ProcessSingleMessageAsync(item);

                    if (!_options.ContinueOnFailure && item.CompletionSource.Task.IsCompleted)
                    {
                        var result = await item.CompletionSource.Task;
                        if (!result.Success)
                        {
                            _logger.LogWarning(
                                "Batch processing stopped at message {MessageId} due to failure",
                                item.Message.MessageId);
                            break;
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

        _batchSemaphore.Dispose();
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
