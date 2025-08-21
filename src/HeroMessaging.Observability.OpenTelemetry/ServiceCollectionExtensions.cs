using HeroMessaging.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HeroMessaging.Observability.OpenTelemetry;

/// <summary>
/// Extension methods for registering OpenTelemetry instrumentation
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add OpenTelemetry instrumentation to HeroMessaging
    /// </summary>
    public static IHeroMessagingBuilder AddOpenTelemetry(this IHeroMessagingBuilder builder)
    {
        // TODO: Register OpenTelemetry decorator when decorator pattern is implemented
        // This will provide OpenTelemetry instrumentation for message processing
        
        return builder;
    }
}