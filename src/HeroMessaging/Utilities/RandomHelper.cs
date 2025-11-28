namespace HeroMessaging.Utilities;

/// <summary>
/// Helper class for Random compatibility across different framework versions
/// </summary>
internal static class RandomHelper
{
    /// <summary>
    /// Gets the shared Random instance
    /// </summary>
    public static Random Instance => Random.Shared;
}
