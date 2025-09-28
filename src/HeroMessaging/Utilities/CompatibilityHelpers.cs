namespace HeroMessaging.Utilities;

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