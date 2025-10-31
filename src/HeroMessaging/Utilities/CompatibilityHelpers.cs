namespace HeroMessaging.Utilities;

/// <summary>
/// Provides compatibility helpers for cross-framework functionality across .NET Standard 2.0 and modern .NET
/// </summary>
/// <remarks>
/// This internal utility class bridges API differences between .NET Standard 2.0 and newer .NET versions,
/// enabling the library to target multiple frameworks with a single codebase. Uses conditional compilation
/// to provide framework-specific implementations that expose a unified API surface.
/// </remarks>
internal static class CompatibilityHelpers
{
#if NETSTANDARD2_0
    public static void ThrowIfNull(object? argument, string paramName)
    {
        if (argument == null)
        {
            throw new ArgumentNullException(paramName);
        }
    }

    public static ValueTask<T> FromResult<T>(T result)
    {
        return new ValueTask<T>(result);
    }

    public static void SetCanceled<T>(this TaskCompletionSource<T> tcs, CancellationToken cancellationToken)
    {
        tcs.SetCanceled();
    }

    public static bool Contains(this string text, string value, StringComparison comparison)
    {
        return text?.IndexOf(value, comparison) >= 0;
    }
#else
    public static void ThrowIfNull(object? argument, string paramName)
    {
        ArgumentNullException.ThrowIfNull(argument, paramName);
    }

    public static ValueTask<T> FromResult<T>(T result)
    {
        return ValueTask.FromResult(result);
    }

    public static void SetCanceled<T>(this TaskCompletionSource<T> tcs, CancellationToken cancellationToken)
    {
        tcs.SetCanceled(cancellationToken);
    }

    public static bool Contains(this string text, string value, StringComparison comparison)
    {
        return text?.Contains(value, comparison) ?? false;
    }
#endif
}