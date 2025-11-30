using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.ErrorHandling;

public interface IErrorHandler
{
    Task<ErrorHandlingResult> HandleErrorAsync<T>(T message, Exception error, ErrorContext context, CancellationToken cancellationToken = default) where T : IMessage;
}

public sealed record ErrorContext
{
    public int RetryCount { get; init; }
    public int MaxRetries { get; init; }
    public string Component { get; init; } = string.Empty;
    public string? QueueName { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = [];
    public DateTimeOffset FirstFailureTime { get; init; }
    public DateTimeOffset LastFailureTime { get; init; }
}

public sealed record ErrorHandlingResult
{
    public ErrorAction Action { get; init; }
    public TimeSpan? RetryDelay { get; init; }
    public string? Reason { get; init; }

    public static ErrorHandlingResult Retry(TimeSpan delay) => new() { Action = ErrorAction.Retry, RetryDelay = delay };
    public static ErrorHandlingResult SendToDeadLetter(string reason) => new() { Action = ErrorAction.SendToDeadLetter, Reason = reason };
    public static ErrorHandlingResult Discard(string reason) => new() { Action = ErrorAction.Discard, Reason = reason };
    public static ErrorHandlingResult Escalate() => new() { Action = ErrorAction.Escalate };
}

public enum ErrorAction
{
    Retry,
    SendToDeadLetter,
    Discard,
    Escalate
}
