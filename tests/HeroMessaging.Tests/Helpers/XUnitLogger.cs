using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroMessaging.Tests.Helpers;

/// <summary>
/// Logger that writes to xUnit test output
/// </summary>
public class XUnitLogger<T> : ILogger<T>, IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public XUnitLogger(ITestOutputHelper output)
    {
        _output = output;
        _categoryName = typeof(T).Name;
    }

    public IDisposable BeginScope<TState>(TState state) => this;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        try
        {
            var message = formatter(state, exception);
            _output.WriteLine($"[{logLevel}] {_categoryName}: {message}");

            if (exception != null)
            {
                _output.WriteLine($"Exception: {exception}");
            }
        }
        catch
        {
            // Ignore logging errors in tests
        }
    }

    public void Dispose() { }
}

/// <summary>
/// Extension method for adding xUnit logger to logging builder
/// </summary>
public static class XUnitLoggerExtensions
{
    public static ILoggingBuilder AddXUnit(this ILoggingBuilder builder, ITestOutputHelper output)
    {
        builder.AddProvider(new XUnitLoggerProvider(output));
        return builder;
    }
}

/// <summary>
/// Logger provider for xUnit test output
/// </summary>
public class XUnitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;

    public XUnitLoggerProvider(ITestOutputHelper output)
    {
        _output = output;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XUnitLoggerGeneric(_output, categoryName);
    }

    public void Dispose() { }
}

/// <summary>
/// Non-generic logger implementation
/// </summary>
internal class XUnitLoggerGeneric : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public XUnitLoggerGeneric(ITestOutputHelper output, string categoryName)
    {
        _output = output;
        _categoryName = categoryName;
    }

    public IDisposable BeginScope<TState>(TState state) => new NoOpDisposable();

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        try
        {
            var message = formatter(state, exception);
            _output.WriteLine($"[{logLevel}] {_categoryName}: {message}");

            if (exception != null)
            {
                _output.WriteLine($"Exception: {exception}");
            }
        }
        catch
        {
            // Ignore logging errors in tests
        }
    }

    private class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
