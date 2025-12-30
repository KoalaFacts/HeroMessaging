using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Processing;

/// <summary>
/// High-performance message processor interface using ValueTask
/// </summary>
public interface IMessageProcessor
{
    /// <summary>
    /// Process a message through the pipeline
    /// </summary>
    ValueTask<ProcessingResult> ProcessAsync(IMessage message, ProcessingContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optimized processing context using struct to minimize heap allocations.
/// </summary>
public readonly record struct ProcessingContext
{
    /// <summary>
    /// Initializes a new default processing context.
    /// </summary>
    public ProcessingContext()
    {
        Component = string.Empty;
        Handler = null;
        HandlerType = null;
        RetryCount = 0;
        FirstFailureTime = null;
        Metadata = ImmutableDictionary<string, object>.Empty;
    }

    /// <summary>
    /// Initializes a new processing context with the specified component name.
    /// </summary>
    /// <param name="component">The name of the processing component</param>
    /// <param name="metadata">Optional metadata dictionary</param>
    public ProcessingContext(string component, ImmutableDictionary<string, object>? metadata = null)
    {
        Component = component;
        Handler = null;
        HandlerType = null;
        RetryCount = 0;
        FirstFailureTime = null;
        Metadata = metadata ?? ImmutableDictionary<string, object>.Empty;
    }

    /// <summary>
    /// Gets the name of the processing component.
    /// </summary>
    public string Component { get; init; }

    /// <summary>
    /// Gets the handler instance processing the message.
    /// </summary>
    public object? Handler { get; init; }

    /// <summary>
    /// Gets the type of the handler processing the message.
    /// </summary>
    public Type? HandlerType { get; init; }

    /// <summary>
    /// Gets the number of retry attempts for this message.
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// Gets when the first failure occurred for retry tracking.
    /// </summary>
    public DateTimeOffset? FirstFailureTime { get; init; }

    /// <summary>
    /// Gets the immutable metadata dictionary for the processing context.
    /// </summary>
    public ImmutableDictionary<string, object> Metadata { get; init; } = ImmutableDictionary<string, object>.Empty;

    /// <summary>
    /// Creates a new context with the specified metadata added.
    /// </summary>
    /// <param name="key">The metadata key</param>
    /// <param name="value">The metadata value</param>
    /// <returns>A new context with the metadata added</returns>
    public ProcessingContext WithMetadata(string key, object value)
    {
        return this with { Metadata = Metadata.SetItem(key, value) };
    }

    /// <summary>
    /// Creates a new context with updated retry information.
    /// </summary>
    /// <param name="retryCount">The current retry count</param>
    /// <param name="firstFailureTime">When the first failure occurred</param>
    /// <returns>A new context with retry information</returns>
    public ProcessingContext WithRetry(int retryCount, DateTimeOffset? firstFailureTime = null)
    {
        return this with
        {
            RetryCount = retryCount,
            FirstFailureTime = firstFailureTime ?? FirstFailureTime
        };
    }

    /// <summary>
    /// Gets a value type metadata value by key.
    /// </summary>
    /// <typeparam name="T">The value type</typeparam>
    /// <param name="key">The metadata key</param>
    /// <returns>The value if found and of correct type; otherwise, null</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetMetadata<T>(string key) where T : struct
    {
        return Metadata.TryGetValue(key, out var value) && value is T typedValue
            ? typedValue
            : default(T?);
    }

    /// <summary>
    /// Gets a reference type metadata value by key.
    /// </summary>
    /// <typeparam name="T">The reference type</typeparam>
    /// <param name="key">The metadata key</param>
    /// <returns>The value if found and of correct type; otherwise, null</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetMetadataReference<T>(string key) where T : class
    {
        return Metadata.TryGetValue(key, out var value) ? value as T : null;
    }
}

/// <summary>
/// Processing result as struct to avoid heap allocations.
/// </summary>
public readonly record struct ProcessingResult
{
    /// <summary>
    /// Gets whether the processing was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the exception if processing failed.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Gets an optional result message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets optional result data.
    /// </summary>
    public object? Data { get; init; }

    /// <summary>
    /// Creates a successful processing result.
    /// </summary>
    /// <param name="message">Optional success message</param>
    /// <param name="data">Optional result data</param>
    /// <returns>A successful processing result</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ProcessingResult Successful(string? message = null, object? data = null)
        => new() { Success = true, Message = message, Data = data };

    /// <summary>
    /// Creates a failed processing result.
    /// </summary>
    /// <param name="exception">The exception that caused the failure</param>
    /// <param name="message">Optional error message</param>
    /// <returns>A failed processing result</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ProcessingResult Failed(Exception exception, string? message = null)
        => new() { Success = false, Exception = exception, Message = message };
}
