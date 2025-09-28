using HeroMessaging.Abstractions.Serialization;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HeroMessaging.Serialization.MessagePack;

/// <summary>
/// Extension methods for registering MessagePack serialization
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add MessagePack serialization support to HeroMessaging
    /// </summary>
    public static IServiceCollection AddHeroMessagingMessagePackSerializer(
        this IServiceCollection services,
        SerializationOptions? options = null,
        MessagePackSerializerOptions? messagePackOptions = null)
    {
        services.TryAddSingleton<IMessageSerializer>(sp =>
            new MessagePackMessageSerializer(options, messagePackOptions));

        return services;
    }

    /// <summary>
    /// Add MessagePack contract serialization support to HeroMessaging
    /// </summary>
    public static IServiceCollection AddHeroMessagingContractMessagePackSerializer(
        this IServiceCollection services,
        SerializationOptions? options = null,
        MessagePackSerializerOptions? messagePackOptions = null)
    {
        services.TryAddSingleton<IMessageSerializer>(sp =>
            new ContractMessagePackSerializer(options, messagePackOptions));

        return services;
    }

    /// <summary>
    /// Add MessagePack serialization support with custom configuration
    /// </summary>
    public static IServiceCollection AddHeroMessagingMessagePackSerializer(
        this IServiceCollection services,
        Action<SerializationOptions> configureOptions,
        Action<MessagePackSerializerOptions>? configureMessagePackOptions = null)
    {
        var options = new SerializationOptions();
        configureOptions(options);

        MessagePackSerializerOptions? messagePackOptions = null;
        if (configureMessagePackOptions != null)
        {
            messagePackOptions = MessagePackSerializerOptions.Standard;
            configureMessagePackOptions(messagePackOptions);
        }

        return services.AddHeroMessagingMessagePackSerializer(options, messagePackOptions);
    }
}