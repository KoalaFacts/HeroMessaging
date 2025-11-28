using HeroMessaging.Abstractions.Serialization;
using HeroMessaging.Serialization.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoBuf.Meta;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Protobuf serialization
/// </summary>
// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130 // Namespace does not match folder structure
public static class ExtensionsToIServiceCollectionForProtobufSerialization
{
    /// <summary>
    /// Add Protobuf serialization support to HeroMessaging
    /// </summary>
    public static IServiceCollection AddHeroMessagingProtobufSerializer(
        this IServiceCollection services,
        SerializationOptions? options = null,
        RuntimeTypeModel? typeModel = null)
    {
        services.TryAddSingleton<IMessageSerializer>(sp =>
            new ProtobufMessageSerializer(options, typeModel));

        return services;
    }

    /// <summary>
    /// Add Typed Protobuf serialization support to HeroMessaging
    /// </summary>
    public static IServiceCollection AddHeroMessagingTypedProtobufSerializer(
        this IServiceCollection services,
        SerializationOptions? options = null,
        RuntimeTypeModel? typeModel = null)
    {
        services.TryAddSingleton<IMessageSerializer>(sp =>
            new TypedProtobufMessageSerializer(options, typeModel));

        return services;
    }

    /// <summary>
    /// Add Protobuf serialization support with custom configuration
    /// </summary>
    public static IServiceCollection AddHeroMessagingProtobufSerializer(
        this IServiceCollection services,
        Action<SerializationOptions> configureOptions,
        Action<RuntimeTypeModel>? configureTypeModel = null)
    {
        var options = new SerializationOptions();
        configureOptions(options);

        RuntimeTypeModel? typeModel = null;
        if (configureTypeModel != null)
        {
            typeModel = RuntimeTypeModel.Create();
            configureTypeModel(typeModel);
        }

        return services.AddHeroMessagingProtobufSerializer(options, typeModel);
    }
}