using HeroMessaging.Abstractions.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;

namespace HeroMessaging.Serialization.Json;

/// <summary>
/// Extension methods for registering JSON serialization
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add JSON serialization support to HeroMessaging
    /// </summary>
    public static IServiceCollection AddHeroMessagingJsonSerializer(
        this IServiceCollection services,
        SerializationOptions? options = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        services.TryAddSingleton<IMessageSerializer>(sp =>
            new JsonMessageSerializer(options, jsonOptions));

        return services;
    }

    /// <summary>
    /// Add JSON serialization support with custom configuration
    /// </summary>
    public static IServiceCollection AddHeroMessagingJsonSerializer(
        this IServiceCollection services,
        Action<SerializationOptions> configureOptions,
        Action<JsonSerializerOptions>? configureJsonOptions = null)
    {
        var options = new SerializationOptions();
        configureOptions(options);

        JsonSerializerOptions? jsonOptions = null;
        if (configureJsonOptions != null)
        {
            jsonOptions = new JsonSerializerOptions();
            configureJsonOptions(jsonOptions);
        }

        return services.AddHeroMessagingJsonSerializer(options, jsonOptions);
    }
}