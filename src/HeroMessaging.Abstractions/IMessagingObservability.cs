namespace HeroMessaging.Abstractions;

/// <summary>
/// Provides observability capabilities for the messaging system.
/// </summary>
/// <remarks>
/// This interface is a subset of <see cref="IHeroMessaging"/> for consumers
/// that only need to monitor the messaging system. Use this for dependency injection when
/// your component only needs metrics and health information.
/// </remarks>
public interface IMessagingObservability
{
    /// <summary>
    /// Gets the current messaging metrics including message counts and processing statistics.
    /// </summary>
    /// <returns>A snapshot of the current messaging metrics.</returns>
    MessagingMetrics GetMetrics();

    /// <summary>
    /// Gets the current health status of all messaging components.
    /// </summary>
    /// <returns>The health status of the messaging system and its components.</returns>
    MessagingHealth GetHealth();
}
