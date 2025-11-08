// Copyright (c) HeroMessaging Contributors. All rights reserved.

using System;

namespace HeroMessaging.SourceGenerators;

/// <summary>
/// Generates logging code for a method, including entry/exit/duration/errors.
/// Creates a partial method implementation with structured logging.
/// </summary>
/// <example>
/// <code>
/// [LogMethod(LogLevel.Information)]
/// public partial Task ProcessOrderAsync(string orderId, decimal amount);
///
/// // Generated implementation:
/// public partial async Task ProcessOrderAsync(string orderId, decimal amount)
/// {
///     using var activity = new Activity("ProcessOrderAsync");
///     var stopwatch = Stopwatch.StartNew();
///
///     _logger.LogInformation("Processing order {OrderId} with amount {Amount}", orderId, amount);
///
///     try
///     {
///         var result = await ProcessOrderCoreAsync(orderId, amount);
///
///         _logger.LogInformation(
///             "Completed processing order {OrderId} in {DurationMs}ms",
///             orderId, stopwatch.ElapsedMilliseconds);
///
///         return result;
///     }
///     catch (Exception ex)
///     {
///         _logger.LogError(ex,
///             "Failed processing order {OrderId} after {DurationMs}ms",
///             orderId, stopwatch.ElapsedMilliseconds);
///         throw;
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LogMethodAttribute : Attribute
{
    /// <summary>
    /// The log level for entry/exit messages.
    /// </summary>
    public LogLevel Level { get; }

    /// <summary>
    /// Whether to log method parameters (default: true).
    /// </summary>
    public bool LogParameters { get; set; } = true;

    /// <summary>
    /// Whether to log method duration (default: true).
    /// </summary>
    public bool LogDuration { get; set; } = true;

    /// <summary>
    /// Whether to log method result (default: false, to avoid large payloads).
    /// </summary>
    public bool LogResult { get; set; } = false;

    /// <summary>
    /// Whether to create an Activity span for distributed tracing (default: true).
    /// </summary>
    public bool CreateActivity { get; set; } = true;

    /// <summary>
    /// Custom message template for entry log. Use {ParameterName} for parameters.
    /// </summary>
    public string? EntryMessage { get; set; }

    /// <summary>
    /// Custom message template for exit log.
    /// </summary>
    public string? ExitMessage { get; set; }

    public LogMethodAttribute(LogLevel level = LogLevel.Information)
    {
        Level = level;
    }
}

/// <summary>
/// Log levels matching Microsoft.Extensions.Logging.LogLevel.
/// </summary>
public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    None = 6
}

/// <summary>
/// Excludes a parameter from automatic logging (for sensitive data).
/// </summary>
/// <example>
/// <code>
/// [LogMethod]
/// public partial Task ProcessPaymentAsync(
///     string orderId,
///     [NoLog] string creditCardNumber); // Won't be logged
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class NoLogAttribute : Attribute
{
}

/// <summary>
/// Generates a high-level logging wrapper class for a service.
/// Creates logging delegates for all public methods.
/// </summary>
/// <example>
/// <code>
/// [GenerateLogger]
/// public partial class OrderService
/// {
///     // Your service implementation
///     public Task CreateOrderAsync(string orderId) { ... }
/// }
///
/// // Generated:
/// public partial class OrderService
/// {
///     private static readonly Action&lt;ILogger, string, Exception?&gt; _logCreateOrder =
///         LoggerMessage.Define&lt;string&gt;(
///             LogLevel.Information,
///             new EventId(1, nameof(CreateOrderAsync)),
///             "Creating order {OrderId}");
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class GenerateLoggerAttribute : Attribute
{
    /// <summary>
    /// The default log level for all methods (can be overridden per method).
    /// </summary>
    public LogLevel DefaultLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Whether to generate LoggerMessage.Define delegates for performance.
    /// </summary>
    public bool UseLoggerMessage { get; set; } = true;

    /// <summary>
    /// Base event ID for generated log events.
    /// </summary>
    public int BaseEventId { get; set; } = 1000;
}
