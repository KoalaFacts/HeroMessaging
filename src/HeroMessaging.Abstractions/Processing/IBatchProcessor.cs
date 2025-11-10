using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Processing;

/// <summary>
/// High-performance batch message processor interface using ValueTask
/// </summary>
/// <remarks>
/// <para>
/// This interface enables efficient batch processing of multiple messages while maintaining
/// the full processing pipeline for each individual message in the batch.
/// </para>
/// <para>
/// <strong>Design Principles</strong>:
/// </para>
/// <list type="bullet">
/// <item><description>Each message maintains full decorator chain (validation, retry, circuit breaker, etc.)</description></item>
/// <item><description>Thread-safe accumulation with configurable batch size and timeout</description></item>
/// <item><description>Performance target: 20-40% throughput improvement for batch-friendly workloads</description></item>
/// <item><description>Zero-allocation paths using ValueTask and struct-based contexts</description></item>
/// </list>
/// </remarks>
public interface IBatchProcessor
{
    /// <summary>
    /// Process a batch of messages through the pipeline
    /// </summary>
    /// <param name="messages">The collection of messages to process</param>
    /// <param name="contexts">The processing contexts for each message (must match messages count)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Batch processing result containing individual message results</returns>
    /// <exception cref="ArgumentNullException">Thrown when messages or contexts is null</exception>
    /// <exception cref="ArgumentException">Thrown when messages and contexts counts don't match</exception>
    ValueTask<BatchProcessingResult> ProcessBatchAsync(
        IReadOnlyList<IMessage> messages,
        IReadOnlyList<ProcessingContext> contexts,
        CancellationToken cancellationToken = default);
}
