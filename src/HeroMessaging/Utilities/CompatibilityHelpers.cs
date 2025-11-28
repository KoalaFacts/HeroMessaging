namespace HeroMessaging.Utilities;

internal static class CompatibilityHelpers
{
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
}
