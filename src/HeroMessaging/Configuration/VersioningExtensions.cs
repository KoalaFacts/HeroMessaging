using HeroMessaging.Abstractions.Configuration;
using HeroMessaging.Abstractions.Versioning;
using HeroMessaging.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HeroMessaging.Configuration;

/// <summary>
/// Extension methods for configuring message versioning in HeroMessaging
/// </summary>
public static class VersioningExtensions
{
    /// <summary>
    /// Adds message versioning support to HeroMessaging
    /// Registers core versioning services including version resolution, converter registry, and versioned message handling
    /// </summary>
    /// <param name="builder">HeroMessaging builder to configure</param>
    /// <param name="options">Optional versioning configuration options. If null, default options are used</param>
    /// <returns>Builder instance for method chaining</returns>
    /// <remarks>
    /// Registers the following services:
    /// - IMessageVersionResolver: Resolves message versions from metadata
    /// - IMessageConverterRegistry: Manages registered message converters
    /// - IVersionedMessageService: Handles version conversion and compatibility checking
    /// Default configuration enables automatic conversion with backward compatibility mode
    /// </remarks>
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
    /// Provides fluent API for configuring versioning options
    /// </summary>
    /// <param name="builder">HeroMessaging builder to configure</param>
    /// <param name="configure">Configuration action to customize versioning options</param>
    /// <returns>Builder instance for method chaining</returns>
    /// <remarks>
    /// Example usage:
    /// builder.WithVersioning(options => {
    ///     options.EnableAutomaticConversion = true;
    ///     options.StrictValidation = false;
    /// });
    /// </remarks>
    public static IHeroMessagingBuilder WithVersioning(
        this IHeroMessagingBuilder builder,
        Action<MessageVersioningOptions> configure)
    {
        var options = new MessageVersioningOptions();
        configure(options);
        return builder.WithVersioning(options);
    }

    /// <summary>
    /// Registers a message converter for handling version transformations
    /// </summary>
    /// <typeparam name="TMessage">Message type that the converter handles</typeparam>
    /// <param name="builder">HeroMessaging builder to configure</param>
    /// <param name="converter">Message converter instance to register</param>
    /// <returns>Builder instance for method chaining</returns>
    /// <remarks>
    /// Converters enable transformation between different message versions.
    /// Multiple converters can be registered for the same message type to create conversion paths.
    /// Automatically ensures the converter registry service is registered
    /// </remarks>
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
    /// Convenience method for bulk converter registration
    /// </summary>
    /// <typeparam name="TMessage">Message type that the converters handle</typeparam>
    /// <param name="builder">HeroMessaging builder to configure</param>
    /// <param name="converters">Array of message converter instances to register</param>
    /// <returns>Builder instance for method chaining</returns>
    /// <remarks>
    /// Useful for registering an entire conversion chain in one call.
    /// Example: RegisterConverters(v1ToV2Converter, v2ToV3Converter, v3ToV4Converter)
    /// </remarks>
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
    /// Optimized for flexibility and detailed logging during development
    /// </summary>
    /// <param name="builder">HeroMessaging builder to configure</param>
    /// <returns>Builder instance for method chaining</returns>
    /// <remarks>
    /// Development configuration:
    /// - Automatic conversion enabled
    /// - Backward compatibility mode
    /// - Strict validation disabled for flexibility
    /// - Versioning activity logging enabled for troubleshooting
    /// Not recommended for production due to relaxed validation
    /// </remarks>
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
    /// Optimized for safety and correctness in production environments
    /// </summary>
    /// <param name="builder">HeroMessaging builder to configure</param>
    /// <returns>Builder instance for method chaining</returns>
    /// <remarks>
    /// Production configuration:
    /// - Automatic conversion enabled
    /// - Strict validation enabled for data integrity
    /// - Strict compatibility mode (versions must match exactly)
    /// - Versioning activity logging disabled for performance
    /// - 30-second conversion timeout
    /// Recommended for production deployments requiring high reliability
    /// </remarks>
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
    /// Allows newer message handlers to process older message versions
    /// </summary>
    /// <param name="builder">HeroMessaging builder to configure</param>
    /// <returns>Builder instance for method chaining</returns>
    /// <remarks>
    /// Backward compatibility configuration:
    /// - Automatic conversion enabled
    /// - Backward compatibility mode
    /// - Version downgrades allowed
    /// - Strict validation disabled for flexibility
    /// Useful for rolling deployments where new services receive old messages
    /// </remarks>
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
    /// Allows older message handlers to process newer message versions
    /// </summary>
    /// <param name="builder">HeroMessaging builder to configure</param>
    /// <returns>Builder instance for method chaining</returns>
    /// <remarks>
    /// Forward compatibility configuration:
    /// - Automatic conversion enabled
    /// - Forward compatibility mode
    /// - Unknown properties ignored (graceful degradation)
    /// - Strict validation disabled for flexibility
    /// Useful when old services need to handle messages from newer versions
    /// </remarks>
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
    /// When true, the versioning system automatically converts messages between versions using registered converters
    /// </summary>
    /// <value>Default: true</value>
    public bool EnableAutomaticConversion { get; set; } = true;

    /// <summary>
    /// Default compatibility mode for version checking
    /// Determines how version mismatches are handled when no specific converter is available
    /// </summary>
    /// <value>Default: VersionCompatibilityMode.Backward</value>
    public VersionCompatibilityMode DefaultCompatibilityMode { get; set; } = VersionCompatibilityMode.Backward;

    /// <summary>
    /// Whether to perform strict validation on version constraints
    /// When true, enforces semantic versioning rules and compatibility constraints
    /// </summary>
    /// <value>Default: true</value>
    public bool StrictValidation { get; set; } = true;

    /// <summary>
    /// Whether to allow version downgrades
    /// When true, permits converting newer message versions to older versions
    /// </summary>
    /// <value>Default: false</value>
    public bool AllowVersionDowngrades { get; set; } = false;

    /// <summary>
    /// Whether to ignore unknown properties during deserialization
    /// When true, silently discards properties not present in the target message schema
    /// </summary>
    /// <value>Default: true</value>
    public bool IgnoreUnknownProperties { get; set; } = true;

    /// <summary>
    /// Whether to log versioning activity
    /// When true, logs version conversions and compatibility checks for troubleshooting
    /// </summary>
    /// <value>Default: false (disabled for performance in production)</value>
    public bool LogVersioningActivity { get; set; } = false;

    /// <summary>
    /// Timeout for version conversion operations
    /// Prevents hanging on slow or complex conversion chains
    /// </summary>
    /// <value>Default: 10 seconds</value>
    public TimeSpan ConversionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum number of conversion steps allowed in a conversion path
    /// Limits the depth of chained conversions (e.g., v1 -> v2 -> v3 -> v4)
    /// </summary>
    /// <value>Default: 5 steps</value>
    public int MaxConversionSteps { get; set; } = 5;

    /// <summary>
    /// Registered converters collection
    /// Internal collection of converter registration actions applied during service initialization
    /// </summary>
    internal List<Action<IMessageConverterRegistry>> ConverterRegistrations { get; } = new();

    /// <summary>
    /// Registers a converter with the options
    /// Adds converter registration action to be applied when versioning services are initialized
    /// </summary>
    /// <typeparam name="TMessage">Message type that the converter handles</typeparam>
    /// <param name="converter">Message converter instance to register</param>
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
    /// Optimized for distributed systems with independent service deployments and backward compatibility requirements
    /// </summary>
    /// <remarks>
    /// Configuration:
    /// - Automatic conversion enabled
    /// - Backward compatibility mode for rolling deployments
    /// - Strict validation for data integrity
    /// - Version downgrades disabled
    /// - Unknown properties ignored for forward compatibility
    /// - 5-second conversion timeout
    /// - Maximum 3 conversion steps
    /// </remarks>
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
    /// Optimized for single-deployment scenarios where all components update simultaneously
    /// </summary>
    /// <remarks>
    /// Configuration:
    /// - Automatic conversion enabled
    /// - Strict compatibility mode (exact version matching)
    /// - Strict validation enabled
    /// - Version downgrades disabled
    /// - Unknown properties rejected for strictness
    /// - 15-second conversion timeout
    /// - Maximum 5 conversion steps
    /// - Versioning activity logging enabled
    /// </remarks>
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
    /// Optimized for reading historical events with different versions and converting them to current schemas
    /// </summary>
    /// <remarks>
    /// Configuration:
    /// - Automatic conversion enabled
    /// - Forward compatibility mode for reading old events
    /// - Strict validation disabled for flexibility with historical data
    /// - Version downgrades allowed for replaying old events
    /// - Unknown properties ignored for schema evolution
    /// - 30-second conversion timeout for complex transformations
    /// - Maximum 10 conversion steps for long event histories
    /// - Versioning activity logging enabled for audit trail
    /// </remarks>
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
    /// Optimized for maximum throughput by disabling automatic conversion and reducing validation overhead
    /// </summary>
    /// <remarks>
    /// Configuration:
    /// - Automatic conversion disabled (requires manual version handling)
    /// - Strict compatibility mode
    /// - Strict validation disabled for performance
    /// - Version downgrades disabled
    /// - Unknown properties ignored
    /// - 1-second conversion timeout
    /// - Maximum 1 conversion step
    /// - No logging overhead
    /// Only use when version compatibility is guaranteed by deployment strategy
    /// </remarks>
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
    /// Optimized for flexibility and troubleshooting during development with extensive logging
    /// </summary>
    /// <remarks>
    /// Configuration:
    /// - Automatic conversion enabled
    /// - Flexible compatibility mode (allows both backward and forward)
    /// - Strict validation disabled for rapid iteration
    /// - Version downgrades allowed for testing
    /// - Unknown properties ignored for schema experimentation
    /// - 1-minute conversion timeout for debugging
    /// - Maximum 10 conversion steps
    /// - Versioning activity logging enabled for troubleshooting
    /// Not recommended for production use
    /// </remarks>
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