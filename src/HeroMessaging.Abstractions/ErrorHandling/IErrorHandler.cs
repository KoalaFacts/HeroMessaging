using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.ErrorHandling;

public interface IErrorHandler
{
    Task<ErrorHandlingResult> HandleError<T>(T message, Exception error, ErrorContext context, CancellationToken cancellationToken = default) where T : IMessage;
}

public class ErrorContext
{
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; }
    public string Component { get; set; } = string.Empty;
    public string? QueueName { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime FirstFailureTime { get; set; }
    public DateTime LastFailureTime { get; set; }
}

public class ErrorHandlingResult
{
    public ErrorAction Action { get; set; }
    public TimeSpan? RetryDelay { get; set; }
    public string? Reason { get; set; }
    
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