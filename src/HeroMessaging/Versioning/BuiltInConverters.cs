using System.Reflection;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Versioning;

/// <summary>
/// Converter that handles property additions with default values
/// </summary>
public class PropertyAdditionConverter<TMessage>(
    MessageVersion fromVersion,
    MessageVersion toVersion,
    ILogger<PropertyAdditionConverter<TMessage>> logger) : MessageConverter<TMessage>
    where TMessage : class, IMessage
{
    private readonly ILogger<PropertyAdditionConverter<TMessage>> _logger = logger;
    private readonly MessageVersionRange _versionRange = new MessageVersionRange(fromVersion, toVersion);

    public override MessageVersionRange SupportedVersionRange => _versionRange;

    public override async Task<TMessage> ConvertAsync(TMessage message, MessageVersion fromVersion, MessageVersion toVersion, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Make it async for future extensibility

        if (fromVersion == toVersion)
            return message;

        _logger.LogDebug("Converting {MessageType} from version {FromVersion} to {ToVersion} using property addition",
            typeof(TMessage).Name, fromVersion, toVersion);

        // For property additions, we typically don't need to do anything
        // The newer version will have default values for new properties
        // This converter mainly serves as a compatibility declaration

        return message;
    }
}

/// <summary>
/// Converter that handles property removals and deprecations
/// </summary>
public class PropertyRemovalConverter<TMessage>(
    MessageVersion fromVersion,
    MessageVersion toVersion,
    IEnumerable<string> removedProperties,
    ILogger<PropertyRemovalConverter<TMessage>> logger) : MessageConverter<TMessage>
    where TMessage : class, IMessage
{
    private readonly ILogger<PropertyRemovalConverter<TMessage>> _logger = logger;
    private readonly MessageVersionRange _versionRange = new MessageVersionRange(fromVersion, toVersion);
    private readonly HashSet<string> _removedProperties = new HashSet<string>(removedProperties);

    public override MessageVersionRange SupportedVersionRange => _versionRange;

    public override async Task<TMessage> ConvertAsync(TMessage message, MessageVersion fromVersion, MessageVersion toVersion, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (fromVersion == toVersion)
            return message;

        _logger.LogDebug("Converting {MessageType} from version {FromVersion} to {ToVersion} with property removal",
            typeof(TMessage).Name, fromVersion, toVersion);

        // For property removals when going from newer to older version,
        // we need to ensure removed properties are not accessed
        // This is mainly a validation step since JSON serialization handles this automatically

        foreach (var removedProperty in _removedProperties)
        {
            _logger.LogDebug("Property '{PropertyName}' was removed in version {ToVersion}",
                removedProperty, toVersion);
        }

        return message;
    }
}

/// <summary>
/// Converter that maps properties between different names
/// </summary>
public class PropertyMappingConverter<TMessage>(
    MessageVersion fromVersion,
    MessageVersion toVersion,
    IReadOnlyDictionary<string, string> propertyMappings,
    ILogger<PropertyMappingConverter<TMessage>> logger) : MessageConverter<TMessage>
    where TMessage : class, IMessage
{
    private readonly ILogger<PropertyMappingConverter<TMessage>> _logger = logger;
    private readonly MessageVersionRange _versionRange = new MessageVersionRange(fromVersion, toVersion);
    private readonly IReadOnlyDictionary<string, string> _propertyMappings = propertyMappings;

    public override MessageVersionRange SupportedVersionRange => _versionRange;

    public override async Task<TMessage> ConvertAsync(TMessage message, MessageVersion fromVersion, MessageVersion toVersion, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (fromVersion == toVersion)
            return message;

        _logger.LogDebug("Converting {MessageType} from version {FromVersion} to {ToVersion} using property mapping",
            typeof(TMessage).Name, fromVersion, toVersion);

        // This is a complex operation that would typically involve:
        // 1. Serializing the message to a dynamic format (JSON, Dictionary)
        // 2. Applying property mappings
        // 3. Deserializing back to the target type

        // For now, we log the mappings that would be applied
        foreach (var mapping in _propertyMappings)
        {
            _logger.LogDebug("Would map property '{OldName}' to '{NewName}'",
                mapping.Key, mapping.Value);
        }

        return message;
    }
}

/// <summary>
/// Converter that applies transformation functions to properties
/// </summary>
public class TransformationConverter<TMessage>(
    MessageVersion fromVersion,
    MessageVersion toVersion,
    IReadOnlyDictionary<string, Func<object?, object?>> transformations,
    ILogger<TransformationConverter<TMessage>> logger) : MessageConverter<TMessage>
    where TMessage : class, IMessage
{
    private readonly ILogger<TransformationConverter<TMessage>> _logger = logger;
    private readonly MessageVersionRange _versionRange = new MessageVersionRange(fromVersion, toVersion);
    private readonly IReadOnlyDictionary<string, Func<object?, object?>> _transformations = transformations;

    public override MessageVersionRange SupportedVersionRange => _versionRange;

    public override async Task<TMessage> ConvertAsync(TMessage message, MessageVersion fromVersion, MessageVersion toVersion, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        if (fromVersion == toVersion)
            return message;

        _logger.LogDebug("Converting {MessageType} from version {FromVersion} to {ToVersion} using transformations",
            typeof(TMessage).Name, fromVersion, toVersion);

        // Apply transformations using reflection
        var messageType = typeof(TMessage);
        var properties = messageType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToDictionary(p => p.Name, p => p);

        foreach (var transformation in _transformations)
        {
            if (properties.TryGetValue(transformation.Key, out var property))
            {
                try
                {
                    var currentValue = property.GetValue(message);
                    var transformedValue = transformation.Value(currentValue);
                    property.SetValue(message, transformedValue);

                    _logger.LogDebug("Applied transformation to property '{PropertyName}'",
                        transformation.Key);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to apply transformation to property '{PropertyName}'",
                        transformation.Key);
                    throw new MessageConversionException(
                        $"Failed to transform property '{transformation.Key}' during version conversion", ex);
                }
            }
        }

        return message;
    }
}

/// <summary>
/// Generic converter builder for creating common conversion scenarios
/// </summary>
public static class MessageConverterBuilder
{
    /// <summary>
    /// Creates a property addition converter
    /// </summary>
    public static PropertyAdditionConverter<TMessage> ForPropertyAddition<TMessage>(
        MessageVersion fromVersion,
        MessageVersion toVersion,
        ILogger<PropertyAdditionConverter<TMessage>> logger)
        where TMessage : class, IMessage
    {
        return new PropertyAdditionConverter<TMessage>(fromVersion, toVersion, logger);
    }

    /// <summary>
    /// Creates a property removal converter
    /// </summary>
    public static PropertyRemovalConverter<TMessage> ForPropertyRemoval<TMessage>(
        MessageVersion fromVersion,
        MessageVersion toVersion,
        IEnumerable<string> removedProperties,
        ILogger<PropertyRemovalConverter<TMessage>> logger)
        where TMessage : class, IMessage
    {
        return new PropertyRemovalConverter<TMessage>(fromVersion, toVersion, removedProperties, logger);
    }

    /// <summary>
    /// Creates a property mapping converter
    /// </summary>
    public static PropertyMappingConverter<TMessage> ForPropertyMapping<TMessage>(
        MessageVersion fromVersion,
        MessageVersion toVersion,
        IReadOnlyDictionary<string, string> propertyMappings,
        ILogger<PropertyMappingConverter<TMessage>> logger)
        where TMessage : class, IMessage
    {
        return new PropertyMappingConverter<TMessage>(fromVersion, toVersion, propertyMappings, logger);
    }

    /// <summary>
    /// Creates a transformation converter
    /// </summary>
    public static TransformationConverter<TMessage> ForTransformation<TMessage>(
        MessageVersion fromVersion,
        MessageVersion toVersion,
        IReadOnlyDictionary<string, Func<object?, object?>> transformations,
        ILogger<TransformationConverter<TMessage>> logger)
        where TMessage : class, IMessage
    {
        return new TransformationConverter<TMessage>(fromVersion, toVersion, transformations, logger);
    }

    /// <summary>
    /// Creates a simple pass-through converter for compatible versions
    /// </summary>
    public static SimplePassThroughConverter<TMessage> ForPassThrough<TMessage>(
        MessageVersion fromVersion,
        MessageVersion toVersion,
        ILogger<SimplePassThroughConverter<TMessage>> logger)
        where TMessage : class, IMessage
    {
        return new SimplePassThroughConverter<TMessage>(fromVersion, toVersion, logger);
    }
}

/// <summary>
/// Simple pass-through converter for compatible versions
/// </summary>
public class SimplePassThroughConverter<TMessage>(
    MessageVersion fromVersion,
    MessageVersion toVersion,
    ILogger<SimplePassThroughConverter<TMessage>> logger) : MessageConverter<TMessage>
    where TMessage : class, IMessage
{
    private readonly ILogger<SimplePassThroughConverter<TMessage>> _logger = logger;
    private readonly MessageVersionRange _versionRange = new MessageVersionRange(fromVersion, toVersion);

    public override MessageVersionRange SupportedVersionRange => _versionRange;

    public override async Task<TMessage> ConvertAsync(TMessage message, MessageVersion fromVersion, MessageVersion toVersion, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        _logger.LogDebug("Pass-through conversion for {MessageType} from version {FromVersion} to {ToVersion}",
            typeof(TMessage).Name, fromVersion, toVersion);

        return message;
    }
}

/// <summary>
/// Exception thrown when message conversion fails
/// </summary>
public class MessageConversionException : Exception
{
    public MessageConversionException() : base("Message conversion failed") { }
    public MessageConversionException(string message) : base(message) { }
    public MessageConversionException(string message, Exception innerException) : base(message, innerException) { }
}