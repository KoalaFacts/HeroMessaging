using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Versioning;
using HeroMessaging.Configuration;
using HeroMessaging.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HeroMessaging.Abstractions.Configuration;

/// <summary>
/// Extension methods for configuring message versioning in HeroMessaging
/// </summary>
// ReSharper disable once CheckNamespace
#pragma warning disable IDE0130 // Namespace does not match folder structure
public static class ExtensionsToIHeroMessagingBuilderForVersioning
{
    /// <summary>
    /// Adds message versioning support to HeroMessaging
    /// </summary>
    public static IHeroMessagingBuilder WithVersioning(
        this IHeroMessagingBuilder builder,
        MessageVersioningOptions? options = null)
    {
        if (builder is not HeroMessagingBuilder heroBuilder)
        {
            throw new ArgumentException("Builder must be of type HeroMessagingBuilder", nameof(builder));
        }

        var services = heroBuilder.Services;
        options ??= new MessageVersioningOptions();

        // Register core versioning services
        services.TryAddSingleton<IMessageVersionResolver, MessageVersionResolver>();
        services.TryAddSingleton<IMessageConverterRegistry, MessageConverterRegistry>();
        services.TryAddSingleton<IVersionedMessageService, VersionedMessageService>();

        // Register options
        services.AddSingleton(options);

        return builder;
    }

    /// <summary>
    /// Adds message versioning with custom configuration
    /// </summary>
    public static IHeroMessagingBuilder WithVersioning(
        this IHeroMessagingBuilder builder,
        Action<MessageVersioningOptions> configure)
    {
        var options = new MessageVersioningOptions();
        configure(options);
        return builder.WithVersioning(options);
    }

    /// <summary>
    /// Registers a message converter
    /// </summary>
    public static IHeroMessagingBuilder RegisterConverter<TMessage>(
        this IHeroMessagingBuilder builder,
        IMessageConverter<TMessage> converter)
        where TMessage : class, HeroMessaging.Abstractions.Messages.IMessage
    {
        if (builder is not HeroMessagingBuilder heroBuilder)
        {
            throw new ArgumentException("Builder must be of type HeroMessagingBuilder", nameof(builder));
        }

        var services = heroBuilder.Services;

        // Ensure versioning is configured
        services.TryAddSingleton<IMessageConverterRegistry, MessageConverterRegistry>();

        // Register the converter as a configuration action
        services.Configure<MessageVersioningOptions>(options =>
        {
            options.RegisterConverter(converter);
        });

        return builder;
    }

    /// <summary>
    /// Registers multiple converters for a message type
    /// </summary>
    public static IHeroMessagingBuilder RegisterConverters<TMessage>(
        this IHeroMessagingBuilder builder,
        params IMessageConverter<TMessage>[] converters)
        where TMessage : class, HeroMessaging.Abstractions.Messages.IMessage
    {
        foreach (var converter in converters)
        {
            builder.RegisterConverter(converter);
        }
        return builder;
    }

    /// <summary>
    /// Adds standard versioning patterns for development
    /// </summary>
    public static IHeroMessagingBuilder WithDevelopmentVersioning(
        this IHeroMessagingBuilder builder)
    {
        return builder.WithVersioning(options =>
        {
            options.EnableAutomaticConversion = true;
            options.StrictValidation = false;
            options.DefaultCompatibilityMode = VersionCompatibilityMode.Backward;
            options.LogVersioningActivity = true;
        });
    }

    /// <summary>
    /// Adds production-ready versioning configuration
    /// </summary>
    public static IHeroMessagingBuilder WithProductionVersioning(
        this IHeroMessagingBuilder builder)
    {
        return builder.WithVersioning(options =>
        {
            options.EnableAutomaticConversion = true;
            options.StrictValidation = true;
            options.DefaultCompatibilityMode = VersionCompatibilityMode.Strict;
            options.LogVersioningActivity = false;
            options.ConversionTimeout = TimeSpan.FromSeconds(30);
        });
    }

    /// <summary>
    /// Adds versioning with backward compatibility focus
    /// </summary>
    public static IHeroMessagingBuilder WithBackwardCompatibleVersioning(
        this IHeroMessagingBuilder builder)
    {
        return builder.WithVersioning(options =>
        {
            options.EnableAutomaticConversion = true;
            options.DefaultCompatibilityMode = VersionCompatibilityMode.Backward;
            options.AllowVersionDowngrades = true;
            options.StrictValidation = false;
        });
    }

    /// <summary>
    /// Adds versioning with forward compatibility support
    /// </summary>
    public static IHeroMessagingBuilder WithForwardCompatibleVersioning(
        this IHeroMessagingBuilder builder)
    {
        return builder.WithVersioning(options =>
        {
            options.EnableAutomaticConversion = true;
            options.DefaultCompatibilityMode = VersionCompatibilityMode.Forward;
            options.IgnoreUnknownProperties = true;
            options.StrictValidation = false;
        });
    }
}

/// <summary>
/// Configuration options for message versioning
/// </summary>
public class MessageVersioningOptions
{
    /// <summary>
    /// Whether to enable automatic version conversion
    /// </summary>
    public bool EnableAutomaticConversion { get; set; } = true;

    /// <summary>
    /// Default compatibility mode for version checking
    /// </summary>
    public VersionCompatibilityMode DefaultCompatibilityMode { get; set; } = VersionCompatibilityMode.Backward;

    /// <summary>
    /// Whether to perform strict validation on version constraints
    /// </summary>
    public bool StrictValidation { get; set; } = true;

    /// <summary>
    /// Whether to allow version downgrades
    /// </summary>
    public bool AllowVersionDowngrades { get; set; } = false;

    /// <summary>
    /// Whether to ignore unknown properties during deserialization
    /// </summary>
    public bool IgnoreUnknownProperties { get; set; } = true;

    /// <summary>
    /// Whether to log versioning activity
    /// </summary>
    public bool LogVersioningActivity { get; set; } = false;

    /// <summary>
    /// Timeout for version conversion operations
    /// </summary>
    public TimeSpan ConversionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum number of conversion steps allowed in a conversion path
    /// </summary>
    public int MaxConversionSteps { get; set; } = 5;

    /// <summary>
    /// Registered converters
    /// </summary>
    internal List<Action<IMessageConverterRegistry>> ConverterRegistrations { get; } = [];

    /// <summary>
    /// Registers a converter with the options
    /// </summary>
    public void RegisterConverter<TMessage>(IMessageConverter<TMessage> converter)
        where TMessage : class, HeroMessaging.Abstractions.Messages.IMessage
    {
        ConverterRegistrations.Add(registry => registry.RegisterConverter(converter));
    }
}

/// <summary>
/// Version compatibility modes
/// </summary>
public enum VersionCompatibilityMode
{
    /// <summary>
    /// Strict compatibility - versions must match exactly
    /// </summary>
    Strict,

    /// <summary>
    /// Backward compatibility - newer versions can handle older messages
    /// </summary>
    Backward,

    /// <summary>
    /// Forward compatibility - older versions can handle newer messages
    /// </summary>
    Forward,

    /// <summary>
    /// Flexible compatibility - allow both backward and forward compatibility
    /// </summary>
    Flexible
}

/// <summary>
/// Configuration profiles for common versioning scenarios
/// </summary>
public static class MessageVersioningProfiles
{
    /// <summary>
    /// Profile for microservices with frequent deployments
    /// </summary>
    public static MessageVersioningOptions Microservices => new()
    {
        EnableAutomaticConversion = true,
        DefaultCompatibilityMode = VersionCompatibilityMode.Backward,
        StrictValidation = true,
        AllowVersionDowngrades = false,
        IgnoreUnknownProperties = true,
        LogVersioningActivity = false,
        ConversionTimeout = TimeSpan.FromSeconds(5),
        MaxConversionSteps = 3
    };

    /// <summary>
    /// Profile for monolithic applications with controlled deployments
    /// </summary>
    public static MessageVersioningOptions Monolith => new()
    {
        EnableAutomaticConversion = true,
        DefaultCompatibilityMode = VersionCompatibilityMode.Strict,
        StrictValidation = true,
        AllowVersionDowngrades = false,
        IgnoreUnknownProperties = false,
        LogVersioningActivity = true,
        ConversionTimeout = TimeSpan.FromSeconds(15),
        MaxConversionSteps = 5
    };

    /// <summary>
    /// Profile for event sourcing scenarios
    /// </summary>
    public static MessageVersioningOptions EventSourcing => new()
    {
        EnableAutomaticConversion = true,
        DefaultCompatibilityMode = VersionCompatibilityMode.Forward,
        StrictValidation = false,
        AllowVersionDowngrades = true,
        IgnoreUnknownProperties = true,
        LogVersioningActivity = true,
        ConversionTimeout = TimeSpan.FromSeconds(30),
        MaxConversionSteps = 10
    };

    /// <summary>
    /// Profile for high-performance scenarios
    /// </summary>
    public static MessageVersioningOptions HighPerformance => new()
    {
        EnableAutomaticConversion = false, // Manual conversion for performance
        DefaultCompatibilityMode = VersionCompatibilityMode.Strict,
        StrictValidation = false,
        AllowVersionDowngrades = false,
        IgnoreUnknownProperties = true,
        LogVersioningActivity = false,
        ConversionTimeout = TimeSpan.FromSeconds(1),
        MaxConversionSteps = 1
    };

    /// <summary>
    /// Profile for development and testing
    /// </summary>
    public static MessageVersioningOptions Development => new()
    {
        EnableAutomaticConversion = true,
        DefaultCompatibilityMode = VersionCompatibilityMode.Flexible,
        StrictValidation = false,
        AllowVersionDowngrades = true,
        IgnoreUnknownProperties = true,
        LogVersioningActivity = true,
        ConversionTimeout = TimeSpan.FromMinutes(1),
        MaxConversionSteps = 10
    };
}
