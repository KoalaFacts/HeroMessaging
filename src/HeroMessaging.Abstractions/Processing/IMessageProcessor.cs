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
/// Optimized processing context using struct to minimize heap allocations
/// </summary>
public readonly record struct ProcessingContext
{
    public ProcessingContext()
    {
        Component = string.Empty;
        Handler = null;
        HandlerType = null;
        RetryCount = 0;
        FirstFailureTime = null;
        Metadata = ImmutableDictionary<string, object>.Empty;
    }

    public ProcessingContext(string component, ImmutableDictionary<string, object>? metadata = null)
    {
        Component = component;
        Handler = null;
        HandlerType = null;
        RetryCount = 0;
        FirstFailureTime = null;
        Metadata = metadata ?? ImmutableDictionary<string, object>.Empty;
    }

    public string Component { get; init; }
    public object? Handler { get; init; }
    public Type? HandlerType { get; init; }
    public int RetryCount { get; init; }
    public DateTime? FirstFailureTime { get; init; }
    public ImmutableDictionary<string, object> Metadata { get; init; } = ImmutableDictionary<string, object>.Empty;

    public ProcessingContext WithMetadata(string key, object value)
    {
        return this with { Metadata = Metadata.SetItem(key, value) };
    }

    public ProcessingContext WithRetry(int retryCount, DateTime? firstFailureTime = null)
    {
        return this with
        {
            RetryCount = retryCount,
            FirstFailureTime = firstFailureTime ?? FirstFailureTime
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetMetadata<T>(string key) where T : struct
    {
        return Metadata.TryGetValue(key, out var value) && value is T typedValue
            ? typedValue
            : default(T?);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetMetadataReference<T>(string key) where T : class
    {
        return Metadata.TryGetValue(key, out var value) ? value as T : null;
    }
}

/// <summary>
/// Processing result as struct to avoid heap allocations
/// </summary>
public readonly record struct ProcessingResult
{
    public bool Success { get; init; }
    public Exception? Exception { get; init; }
    public string? Message { get; init; }
    public object? Data { get; init; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ProcessingResult Successful(string? message = null, object? data = null)
        => new() { Success = true, Message = message, Data = data };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ProcessingResult Failed(Exception exception, string? message = null)
        => new() { Success = false, Exception = exception, Message = message };
}