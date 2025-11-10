using System.Runtime.CompilerServices;

namespace HeroMessaging.Abstractions.Processing;

/// <summary>
/// Batch processing result containing individual message results
/// </summary>
/// <remarks>
/// <para>
/// This struct provides aggregated results for batch processing operations while maintaining
/// individual message results for detailed error handling and reporting.
/// </para>
/// <para>
/// <strong>Performance Considerations</strong>:
/// </para>
/// <list type="bullet">
/// <item><description>Struct-based design to avoid heap allocations</description></item>
/// <item><description>Read-only collections to prevent defensive copies</description></item>
/// <item><description>Lazy success calculation for optimal performance</description></item>
/// </list>
/// </remarks>
public readonly record struct BatchProcessingResult
{
    /// <summary>
    /// Gets the individual processing results for each message in the batch
    /// </summary>
    public required IReadOnlyList<ProcessingResult> Results { get; init; }

    /// <summary>
    /// Gets the total number of messages processed
    /// </summary>
    public int TotalCount => Results.Count;

    /// <summary>
    /// Gets the number of successfully processed messages
    /// </summary>
    public int SuccessCount
    {
        get
        {
            var count = 0;
            foreach (var result in Results)
            {
                if (result.Success) count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Gets the number of failed messages
    /// </summary>
    public int FailureCount => TotalCount - SuccessCount;

    /// <summary>
    /// Gets whether all messages in the batch were processed successfully
    /// </summary>
    public bool AllSucceeded
    {
        get
        {
            foreach (var result in Results)
            {
                if (!result.Success) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Gets whether any message in the batch was processed successfully
    /// </summary>
    public bool AnySucceeded => SuccessCount > 0;

    /// <summary>
    /// Gets whether all messages in the batch failed
    /// </summary>
    public bool AllFailed => SuccessCount == 0;

    /// <summary>
    /// Gets metadata about the batch processing operation
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets optional data associated with the batch processing result
    /// </summary>
    public object? Data { get; init; }

    /// <summary>
    /// Creates a successful batch processing result
    /// </summary>
    /// <param name="results">Individual message results</param>
    /// <param name="message">Optional message</param>
    /// <param name="data">Optional data</param>
    /// <returns>Batch processing result</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BatchProcessingResult Create(
        IReadOnlyList<ProcessingResult> results,
        string? message = null,
        object? data = null)
        => new() { Results = results, Message = message, Data = data };

    /// <summary>
    /// Creates a batch processing result from individual results
    /// </summary>
    /// <param name="results">Individual processing results</param>
    /// <returns>Batch processing result</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BatchProcessingResult FromResults(IReadOnlyList<ProcessingResult> results)
        => new() { Results = results };

    /// <summary>
    /// Gets the result for a specific message index
    /// </summary>
    /// <param name="index">Message index in the batch</param>
    /// <returns>Processing result for the specified message</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ProcessingResult GetResult(int index) => Results[index];

    /// <summary>
    /// Gets all failed results with their indices
    /// </summary>
    /// <returns>Enumerable of tuples containing index and failed result</returns>
    public IEnumerable<(int Index, ProcessingResult Result)> GetFailedResults()
    {
        for (var i = 0; i < Results.Count; i++)
        {
            if (!Results[i].Success)
            {
                yield return (i, Results[i]);
            }
        }
    }

    /// <summary>
    /// Gets all successful results with their indices
    /// </summary>
    /// <returns>Enumerable of tuples containing index and successful result</returns>
    public IEnumerable<(int Index, ProcessingResult Result)> GetSuccessfulResults()
    {
        for (var i = 0; i < Results.Count; i++)
        {
            if (Results[i].Success)
            {
                yield return (i, Results[i]);
            }
        }
    }
}
