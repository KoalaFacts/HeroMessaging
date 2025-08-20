using System;
using System.Threading;

namespace HeroMessaging.Core.Utilities;

/// <summary>
/// Helper class for Random compatibility across different framework versions
/// </summary>
internal static class RandomHelper
{
#if NETSTANDARD2_0
    private static readonly ThreadLocal<Random> ThreadLocalRandom = new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));
    
    /// <summary>
    /// Gets a thread-safe Random instance
    /// </summary>
    public static Random Instance => ThreadLocalRandom.Value!;
#else
    /// <summary>
    /// Gets the shared Random instance (available in .NET 6+)
    /// </summary>
    public static Random Instance => Random.Shared;
#endif
}