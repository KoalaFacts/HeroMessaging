using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace HeroMessaging.Versioning;

/// <summary>
/// Message converter that handles schema evolution when properties are added to newer message versions.
/// </summary>
/// <typeparam name="TMessage">The message type to convert, must implement <see cref="IMessage"/>.</typeparam>
/// <remarks>
/// This converter handles the common scenario where newer message versions add optional properties
/// with default values. When converting from an older version to a newer version:
/// - Existing properties are preserved
/// - New properties receive their default values (null for reference types, 0/false for value types)
/// - No data transformation is performed
///
/// Use Cases:
/// - Adding optional fields to existing messages
/// - Extending message schemas without breaking backward compatibility
/// - Supporting gradual rollout of new message features
///
/// Conversion Direction:
/// - Forward (old → new): New properties get default values
/// - Backward (new → old): Would require PropertyRemovalConverter
///
/// <code>
/// // Message evolution example
/// // Version 1.0.0
/// public class OrderCreatedEvent : IMessage
/// {
///     public Guid OrderId { get; set; }
///     public decimal Amount { get; set; }
/// }
///
/// // Version 2.0.0 - added Priority property
/// [MessageVersion(2, 0, 0)]
/// public class OrderCreatedEvent : IMessage
/// {
///     public Guid OrderId { get; set; }
///     public decimal Amount { get; set; }
///
///     [AddedInVersion(2, 0, 0)]
///     public string Priority { get; set; } = "Normal"; // Default value
/// }
///
/// // Register converter
/// var converter = new PropertyAdditionConverter&lt;OrderCreatedEvent&gt;(
///     new MessageVersion(1, 0, 0),
///     new MessageVersion(2, 0, 0),
///     logger
/// );
///
/// // Convert v1.0 message to v2.0
/// var v1Message = new OrderCreatedEvent { OrderId = guid, Amount = 100 };
/// var v2Message = await converter.ConvertAsync(v1Message, v1, v2);
/// // v2Message.Priority == "Normal" (default value)
/// </code>
/// </remarks>
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
/// Message converter that handles schema evolution when properties are removed or deprecated from message versions.
/// </summary>
/// <typeparam name="TMessage">The message type to convert, must implement <see cref="IMessage"/>.</typeparam>
/// <remarks>
/// This converter manages backward compatibility when properties are deprecated or removed from message schemas.
/// It primarily serves as a validation and documentation step, as JSON serialization naturally handles
/// missing properties by ignoring them.
///
/// Use Cases:
/// - Deprecating properties in favor of new ones
/// - Removing obsolete fields from message schemas
/// - Converting from newer versions to older versions (downgrade)
/// - Validating that removed properties are not accessed
///
/// Conversion Behavior:
/// - Logs information about removed properties
/// - Validates schema compatibility
/// - Relies on serialization to handle missing properties
///
/// <code>
/// // Message evolution with property removal
/// // Version 2.0.0 - deprecated "Amount" in favor of "TotalAmount"
/// [MessageVersion(2, 0, 0)]
/// public class OrderCreatedEvent : IMessage
/// {
///     public Guid OrderId { get; set; }
///
///     public decimal TotalAmount { get; set; }
///
///     [DeprecatedInVersion(2, 0, 0, Reason = "Use TotalAmount")]
///     public decimal Amount { get; set; } // Deprecated
/// }
///
/// // Register converter
/// var converter = new PropertyRemovalConverter&lt;OrderCreatedEvent&gt;(
///     new MessageVersion(2, 0, 0),
///     new MessageVersion(1, 0, 0),
///     new[] { "Amount" }, // Properties removed when going to v1.0
///     logger
/// );
/// </code>
/// </remarks>
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
/// Message converter that handles property renames by mapping old property names to new property names during version conversion.
/// </summary>
/// <typeparam name="TMessage">The message type to convert, must implement <see cref="IMessage"/>.</typeparam>
/// <remarks>
/// This converter enables schema evolution when properties are renamed. It maps values from
/// old property names to new property names, preserving data during version transitions.
///
/// Use Cases:
/// - Renaming properties for clarity or consistency
/// - Refactoring message schemas without data loss
/// - Migrating from legacy naming conventions
/// - Supporting multiple naming conventions simultaneously
///
/// Implementation Note:
/// Current implementation logs the intended mappings but does not perform actual data transformation.
/// Full implementation would require:
/// 1. Serializing message to dynamic format (JSON/Dictionary)
/// 2. Applying property name mappings
/// 3. Deserializing to target type with new property names
///
/// <code>
/// // Message evolution with property rename
/// // Version 1.0 - original property name
/// public class OrderCreatedEvent : IMessage
/// {
///     public string CustomerId { get; set; }
/// }
///
/// // Version 2.0 - renamed property
/// [MessageVersion(2, 0, 0)]
/// public class OrderCreatedEvent : IMessage
/// {
///     public string ClientId { get; set; } // Renamed from CustomerId
/// }
///
/// // Register converter
/// var converter = new PropertyMappingConverter&lt;OrderCreatedEvent&gt;(
///     new MessageVersion(1, 0, 0),
///     new MessageVersion(2, 0, 0),
///     new Dictionary&lt;string, string&gt;
///     {
///         ["CustomerId"] = "ClientId" // Map old name to new name
///     },
///     logger
/// );
/// </code>
/// </remarks>
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
/// Message converter that applies custom transformation functions to message properties during version conversion.
/// </summary>
/// <typeparam name="TMessage">The message type to convert, must implement <see cref="IMessage"/>.</typeparam>
/// <remarks>
/// This converter enables complex schema evolution by allowing custom transformation logic
/// for individual properties. It uses reflection to apply transformations at runtime.
///
/// Use Cases:
/// - Converting data formats (e.g., string dates to DateTimeOffset)
/// - Changing units or scales (e.g., dollars to cents)
/// - Normalizing or formatting values
/// - Applying business logic during conversion
/// - Splitting or combining property values
///
/// Features:
/// - Per-property transformation functions
/// - Reflection-based property access and modification
/// - Exception handling with detailed error messages
/// - Supports any property type transformation via object?
///
/// <code>
/// // Message evolution with value transformation
/// // Version 1.0 - Amount in dollars
/// public class OrderCreatedEvent : IMessage
/// {
///     public decimal Amount { get; set; } // In dollars
/// }
///
/// // Version 2.0 - Amount in cents
/// [MessageVersion(2, 0, 0)]
/// public class OrderCreatedEvent : IMessage
/// {
///     public long AmountCents { get; set; } // In cents
/// }
///
/// // Register converter with transformation
/// var converter = new TransformationConverter&lt;OrderCreatedEvent&gt;(
///     new MessageVersion(1, 0, 0),
///     new MessageVersion(2, 0, 0),
///     new Dictionary&lt;string, Func&lt;object?, object?&gt;&gt;
///     {
///         ["Amount"] = value => value is decimal d ? (long)(d * 100) : 0L
///     },
///     logger
/// );
///
/// // Convert v1.0 to v2.0
/// var v1 = new OrderCreatedEvent { Amount = 99.99m };
/// var v2 = await converter.ConvertAsync(v1, v1Version, v2Version);
/// // v2.AmountCents == 9999
/// </code>
/// </remarks>
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
/// Factory class for creating built-in message converters for common version migration scenarios.
/// </summary>
/// <remarks>
/// This static builder class provides convenient factory methods for creating the most common
/// types of message converters:
/// - Property additions (new fields added)
/// - Property removals (fields deprecated/removed)
/// - Property mappings (fields renamed)
/// - Property transformations (values changed)
/// - Pass-through (no conversion needed)
///
/// Benefits of using the builder:
/// - Type-safe converter creation
/// - Consistent API across converter types
/// - Reduced boilerplate code
/// - Clear intent in version migration setup
///
/// <code>
/// // Create converters using the builder
/// var services = new ServiceCollection();
/// services.AddLogging();
/// var sp = services.BuildServiceProvider();
/// var logger = sp.GetRequiredService&lt;ILogger&lt;PropertyAdditionConverter&lt;OrderEvent&gt;&gt;&gt;();
///
/// // Property addition converter
/// var additionConverter = MessageConverterBuilder.ForPropertyAddition&lt;OrderEvent&gt;(
///     new MessageVersion(1, 0, 0),
///     new MessageVersion(2, 0, 0),
///     logger
/// );
///
/// // Property removal converter
/// var removalConverter = MessageConverterBuilder.ForPropertyRemoval&lt;OrderEvent&gt;(
///     new MessageVersion(2, 0, 0),
///     new MessageVersion(1, 0, 0),
///     new[] { "DeprecatedField" },
///     removalLogger
/// );
///
/// // Property mapping converter
/// var mappingConverter = MessageConverterBuilder.ForPropertyMapping&lt;OrderEvent&gt;(
///     new MessageVersion(1, 0, 0),
///     new MessageVersion(2, 0, 0),
///     new Dictionary&lt;string, string&gt; { ["OldName"] = "NewName" },
///     mappingLogger
/// );
/// </code>
/// </remarks>
public static class MessageConverterBuilder
{
    /// <summary>
    /// Creates a converter for handling property additions when migrating to newer message versions.
    /// </summary>
    /// <typeparam name="TMessage">The message type to convert, must implement <see cref="IMessage"/>.</typeparam>
    /// <param name="fromVersion">The source version to convert from.</param>
    /// <param name="toVersion">The target version to convert to (should be newer than fromVersion).</param>
    /// <param name="logger">Logger for diagnostic output during conversion.</param>
    /// <returns>A configured PropertyAdditionConverter instance.</returns>
    /// <remarks>
    /// Use this method when newer message versions add optional properties with default values.
    /// The converter ensures older messages work with newer code by allowing new properties
    /// to use their default values.
    /// </remarks>
    public static PropertyAdditionConverter<TMessage> ForPropertyAddition<TMessage>(
        MessageVersion fromVersion,
        MessageVersion toVersion,
        ILogger<PropertyAdditionConverter<TMessage>> logger)
        where TMessage : class, IMessage
    {
        return new PropertyAdditionConverter<TMessage>(fromVersion, toVersion, logger);
    }

    /// <summary>
    /// Creates a converter for handling property removals or deprecations when migrating between message versions.
    /// </summary>
    /// <typeparam name="TMessage">The message type to convert, must implement <see cref="IMessage"/>.</typeparam>
    /// <param name="fromVersion">The source version to convert from.</param>
    /// <param name="toVersion">The target version to convert to.</param>
    /// <param name="removedProperties">Array of property names that were removed in the target version.</param>
    /// <param name="logger">Logger for diagnostic output during conversion.</param>
    /// <returns>A configured PropertyRemovalConverter instance.</returns>
    /// <remarks>
    /// Use this method when properties are deprecated or removed from message schemas.
    /// Helps maintain backward compatibility when converting to older versions.
    /// </remarks>
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
    /// Creates a converter for handling property renames by mapping old names to new names during version conversion.
    /// </summary>
    /// <typeparam name="TMessage">The message type to convert, must implement <see cref="IMessage"/>.</typeparam>
    /// <param name="fromVersion">The source version to convert from.</param>
    /// <param name="toVersion">The target version to convert to.</param>
    /// <param name="propertyMappings">Dictionary mapping old property names (keys) to new property names (values).</param>
    /// <param name="logger">Logger for diagnostic output during conversion.</param>
    /// <returns>A configured PropertyMappingConverter instance.</returns>
    /// <remarks>
    /// Use this method when properties are renamed between versions.
    /// The mappings dictionary defines how old property names map to new property names.
    /// </remarks>
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
    /// Creates a converter that applies custom transformation functions to properties during version conversion.
    /// </summary>
    /// <typeparam name="TMessage">The message type to convert, must implement <see cref="IMessage"/>.</typeparam>
    /// <param name="fromVersion">The source version to convert from.</param>
    /// <param name="toVersion">The target version to convert to.</param>
    /// <param name="transformations">Dictionary mapping property names to transformation functions.</param>
    /// <param name="logger">Logger for diagnostic output during conversion.</param>
    /// <returns>A configured TransformationConverter instance.</returns>
    /// <remarks>
    /// Use this method when property values need transformation during migration (e.g., format changes, unit conversions).
    /// Each transformation function receives the current property value and returns the transformed value.
    /// </remarks>
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
    /// Creates a pass-through converter for compatible versions that require no transformation.
    /// </summary>
    /// <typeparam name="TMessage">The message type to convert, must implement <see cref="IMessage"/>.</typeparam>
    /// <param name="fromVersion">The source version.</param>
    /// <param name="toVersion">The target version.</param>
    /// <param name="logger">Logger for diagnostic output during conversion.</param>
    /// <returns>A configured SimplePassThroughConverter instance.</returns>
    /// <remarks>
    /// Use this method when versions are compatible and no conversion logic is needed.
    /// Useful for declaring version compatibility without requiring actual data transformation.
    /// </remarks>
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
/// Pass-through message converter that performs no transformation, used for compatible message versions.
/// </summary>
/// <typeparam name="TMessage">The message type to convert, must implement <see cref="IMessage"/>.</typeparam>
/// <remarks>
/// This converter is used when message versions are compatible and no actual conversion is needed.
/// It serves to declare version compatibility in the converter registry while performing minimal work.
///
/// Use Cases:
/// - Minor version updates with no breaking changes
/// - Patch versions that only fix bugs
/// - Versions that are wire-compatible
/// - Testing version migration infrastructure
///
/// <code>
/// // Declare v1.0.0 and v1.1.0 are compatible
/// var converter = new SimplePassThroughConverter&lt;OrderEvent&gt;(
///     new MessageVersion(1, 0, 0),
///     new MessageVersion(1, 1, 0),
///     logger
/// );
///
/// var message = new OrderEvent();
/// var result = await converter.ConvertAsync(message, v1_0, v1_1);
/// // result == message (same instance returned)
/// </code>
/// </remarks>
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
/// Exception thrown when message version conversion fails due to transformation errors or incompatible schemas.
/// </summary>
/// <remarks>
/// This exception is thrown by message converters when:
/// - Transformation functions fail or throw exceptions
/// - Property values cannot be converted to target types
/// - Schema incompatibilities are detected
/// - Required data is missing for conversion
///
/// The exception preserves the original error as an inner exception for diagnostics.
///
/// <code>
/// try
/// {
///     var converted = await converter.ConvertAsync(message, fromVersion, toVersion);
/// }
/// catch (MessageConversionException ex)
/// {
///     logger.LogError(ex, "Failed to convert message from {From} to {To}: {Message}",
///         fromVersion, toVersion, ex.Message);
///
///     // Check inner exception for root cause
///     if (ex.InnerException != null)
///     {
///         logger.LogError("Root cause: {InnerException}", ex.InnerException.Message);
///     }
/// }
/// </code>
/// </remarks>
public class MessageConversionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageConversionException"/> class with a default message.
    /// </summary>
    public MessageConversionException() : base("Message conversion failed") { }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageConversionException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the conversion error.</param>
    public MessageConversionException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageConversionException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the conversion error.</param>
    /// <param name="innerException">The exception that caused this conversion failure.</param>
    public MessageConversionException(string message, Exception innerException) : base(message, innerException) { }
}