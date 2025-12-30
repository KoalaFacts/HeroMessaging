using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.ErrorHandling;

/// <summary>
/// Handles errors that occur during message processing and determines the appropriate recovery action.
/// </summary>
public interface IErrorHandler
{
    /// <summary>
    /// Handles an error that occurred during message processing.
    /// </summary>
    /// <typeparam name="T">The type of message that failed</typeparam>
    /// <param name="message">The message that failed processing</param>
    /// <param name="error">The exception that occurred</param>
    /// <param name="context">Context information about the error</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The result indicating what action to take</returns>
    Task<ErrorHandlingResult> HandleErrorAsync<T>(T message, Exception error, ErrorContext context, CancellationToken cancellationToken = default) where T : IMessage;
}

/// <summary>
/// Context information about an error that occurred during message processing.
/// </summary>
public sealed record ErrorContext
{
    /// <summary>
    /// Number of times this message has been retried.
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// Maximum number of retries allowed for this message.
    /// </summary>
    public int MaxRetries { get; init; }

    /// <summary>
    /// The component or handler that encountered the error.
    /// </summary>
    public string Component { get; init; } = string.Empty;

    /// <summary>
    /// The name of the queue the message came from, if applicable.
    /// </summary>
    public string? QueueName { get; init; }

    /// <summary>
    /// Additional metadata about the error context.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// When the first failure occurred for this message.
    /// </summary>
    public DateTimeOffset FirstFailureTime { get; init; }

    /// <summary>
    /// When the most recent failure occurred.
    /// </summary>
    public DateTimeOffset LastFailureTime { get; init; }
}

/// <summary>
/// Result of error handling indicating what action should be taken.
/// </summary>
public sealed record ErrorHandlingResult
{
    /// <summary>
    /// The action to take for this error.
    /// </summary>
    public ErrorAction Action { get; init; }

    /// <summary>
    /// Delay before retrying, if Action is Retry.
    /// </summary>
    public TimeSpan? RetryDelay { get; init; }

    /// <summary>
    /// Human-readable reason for the action taken.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Creates a result indicating the message should be retried after a delay.
    /// </summary>
    /// <param name="delay">The delay before retrying</param>
    /// <returns>A retry result</returns>
    public static ErrorHandlingResult Retry(TimeSpan delay) => new() { Action = ErrorAction.Retry, RetryDelay = delay };

    /// <summary>
    /// Creates a result indicating the message should be sent to dead letter queue.
    /// </summary>
    /// <param name="reason">The reason for dead-lettering</param>
    /// <returns>A dead letter result</returns>
    public static ErrorHandlingResult SendToDeadLetter(string reason) => new() { Action = ErrorAction.SendToDeadLetter, Reason = reason };

    /// <summary>
    /// Creates a result indicating the message should be permanently discarded.
    /// </summary>
    /// <param name="reason">The reason for discarding</param>
    /// <returns>A discard result</returns>
    public static ErrorHandlingResult Discard(string reason) => new() { Action = ErrorAction.Discard, Reason = reason };

    /// <summary>
    /// Creates a result indicating the error should be escalated (rethrown).
    /// </summary>
    /// <returns>An escalate result</returns>
    public static ErrorHandlingResult Escalate() => new() { Action = ErrorAction.Escalate };
}

/// <summary>
/// Actions that can be taken in response to an error.
/// </summary>
public enum ErrorAction
{
    /// <summary>
    /// Retry processing the message after a delay.
    /// </summary>
    Retry,

    /// <summary>
    /// Send the message to the dead letter queue for manual inspection.
    /// </summary>
    SendToDeadLetter,

    /// <summary>
    /// Permanently discard the message without further processing.
    /// </summary>
    Discard,

    /// <summary>
    /// Escalate the error by rethrowing the exception.
    /// </summary>
    Escalate
}
