using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;

namespace HeroMessaging.Versioning;

/// <summary>
/// Default implementation of IMessageVersionResolver that resolves message versions from types and instances
/// using attributes, interfaces, and caching for performance.
/// </summary>
/// <remarks>
/// MessageVersionResolver provides high-performance version resolution with automatic caching
/// and support for multiple version detection strategies.
///
/// Version detection strategies (in priority order):
/// 1. MessageVersionAttribute on the message type
/// 2. IVersionedMessage.Version from a default instance
/// 3. Default version 1.0.0
///
/// Performance characteristics:
/// - First resolution: Reflection-based type analysis (~100μs per type)
/// - Cached resolution: Dictionary lookup (~1μs per type)
/// - Thread-safe: Uses ConcurrentDictionary for cache
///
/// <code>
/// // Register in DI
/// services.AddSingleton&lt;IMessageVersionResolver, MessageVersionResolver&gt;();
///
/// // Usage
/// var resolver = serviceProvider.GetRequiredService&lt;IMessageVersionResolver&gt;();
///
/// // Get version from type
/// var version = resolver.GetVersion(typeof(OrderCreatedEvent));
///
/// // Get version from instance
/// var message = new OrderCreatedEvent { ... };
/// var instanceVersion = resolver.GetVersion(message);
///
/// // Get comprehensive version info
/// var info = resolver.GetVersionInfo(typeof(OrderCreatedEvent));
/// </code>
/// </remarks>
public class MessageVersionResolver(ILogger<MessageVersionResolver> logger) : IMessageVersionResolver
{
    private readonly ILogger<MessageVersionResolver> _logger = logger;
    private readonly ConcurrentDictionary<Type, MessageVersion> _typeVersionCache = new();
    private readonly ConcurrentDictionary<Type, MessageVersionInfo> _typeInfoCache = new();


    /// <summary>
    /// Gets the version of a message type by examining attributes and interfaces.
    /// </summary>
    /// <param name="messageType">The message type to get the version for. Must not be null.</param>
    /// <returns>
    /// The MessageVersion for the specified type.
    /// Returns the version from MessageVersionAttribute if present,
    /// otherwise returns 1.0.0 as the default version.
    /// </returns>
    public MessageVersion GetVersion(Type messageType)
    {
        return _typeVersionCache.GetOrAdd(messageType, type =>
        {
            // Check for MessageVersionAttribute
            var versionAttribute = type.GetCustomAttribute<MessageVersionAttribute>();
            if (versionAttribute != null)
            {
                _logger.LogDebug("Found version {Version} for message type {MessageType} from attribute",
                    versionAttribute.Version, type.Name);
                return versionAttribute.Version;
            }

            // Check if the message implements IVersionedMessage
            if (typeof(IVersionedMessage).IsAssignableFrom(type))
            {
                try
                {
                    // Try to create an instance to get the version
                    var instance = Activator.CreateInstance(type);
                    if (instance is IVersionedMessage versionedMessage)
                    {
                        _logger.LogDebug("Found version {Version} for message type {MessageType} from instance",
                            versionedMessage.Version, type.Name);
                        return versionedMessage.Version;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not create instance of {MessageType} to determine version", type.Name);
                }
            }

            // Default to version 1.0.0
            _logger.LogDebug("Using default version 1.0.0 for message type {MessageType}", type.Name);
            return new MessageVersion(1, 0, 0);
        });
    }

    /// <summary>
    /// Gets the version of a message instance, preferring instance-level version over type-level.
    /// </summary>
    /// <param name="message">The message instance to get the version for. Must not be null.</param>
    /// <returns>
    /// The MessageVersion for the specified message instance.
    /// Returns the instance's Version property if it implements IVersionedMessage,
    /// otherwise returns the version of its type.
    /// </returns>
    public MessageVersion GetVersion(IMessage message)
    {
        if (message is IVersionedMessage versionedMessage)
        {
            return versionedMessage.Version;
        }

        return GetVersion(message.GetType());
    }

    /// <summary>
    /// Gets comprehensive version information for a message type, including property-level versioning details.
    /// </summary>
    /// <param name="messageType">The message type to analyze. Must not be null.</param>
    /// <returns>
    /// A MessageVersionInfo object containing the message type, version, type name,
    /// and property-level version information for all public properties.
    /// </returns>
    public MessageVersionInfo GetVersionInfo(Type messageType)
    {
        return _typeInfoCache.GetOrAdd(messageType, type =>
        {
            var version = GetVersion(type);
            var properties = AnalyzeProperties(type);

            return new MessageVersionInfo(
                type,
                version,
                GetMessageTypeName(type),
                properties.ToList().AsReadOnly()
            );
        });
    }

    /// <summary>
    /// Validates that a message instance conforms to the constraints of a target version.
    /// </summary>
    /// <param name="message">The message instance to validate. Must not be null.</param>
    /// <param name="targetVersion">The target version to validate against.</param>
    /// <returns>
    /// A MessageVersionValidationResult containing validation status, errors, and warnings.
    /// </returns>
    public MessageVersionValidationResult ValidateMessage(IMessage message, MessageVersion targetVersion)
    {
        var messageType = message.GetType();
        var versionInfo = GetVersionInfo(messageType);
        var errors = new List<string>();
        var warnings = new List<string>();

        // Check if message version is compatible with target version
        if (!versionInfo.Version.IsCompatibleWith(targetVersion))
        {
            errors.Add($"Message version {versionInfo.Version} is not compatible with target version {targetVersion}");
        }

        // Validate properties based on version constraints
        foreach (var propertyInfo in versionInfo.Properties)
        {
            var property = messageType.GetProperty(propertyInfo.Name);
            if (property == null) continue;

            var value = property.GetValue(message);

            // Check if property was added after target version
            if (propertyInfo.AddedInVersion.HasValue && propertyInfo.AddedInVersion > targetVersion)
            {
                if (value != null && !IsDefaultValue(value, property.PropertyType))
                {
                    errors.Add($"Property '{propertyInfo.Name}' was added in version {propertyInfo.AddedInVersion} " +
                              $"but target version is {targetVersion}");
                }
            }

            // Check deprecated properties
            if (propertyInfo.DeprecatedInVersion.HasValue && targetVersion >= propertyInfo.DeprecatedInVersion)
            {
                if (value != null && !IsDefaultValue(value, property.PropertyType))
                {
                    var warning = $"Property '{propertyInfo.Name}' is deprecated since version {propertyInfo.DeprecatedInVersion}";
                    if (!string.IsNullOrEmpty(propertyInfo.DeprecationReason))
                        warning += $": {propertyInfo.DeprecationReason}";
                    if (!string.IsNullOrEmpty(propertyInfo.ReplacedBy))
                        warning += $" (replaced by {propertyInfo.ReplacedBy})";

                    warnings.Add(warning);
                }
            }
        }

        return new MessageVersionValidationResult(
            errors.Count == 0,
            errors.AsReadOnly(),
            warnings.AsReadOnly()
        );
    }

    /// <summary>
    /// Gets all known versions for a message type.
    /// </summary>
    /// <param name="messageType">The message type to get known versions for. Must not be null.</param>
    /// <returns>
    /// An enumerable of MessageVersion objects representing all known versions.
    /// Currently returns only the current version.
    /// </returns>
    public IEnumerable<MessageVersion> GetKnownVersions(Type messageType)
    {
        // In a more advanced implementation, this could return all known versions
        // from a registry or database. For now, return the current version.
        yield return GetVersion(messageType);
    }

    private IEnumerable<MessagePropertyInfo> AnalyzeProperties(Type messageType)
    {
        var properties = messageType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            var addedInVersion = property.GetCustomAttribute<AddedInVersionAttribute>()?.Version;
            var deprecatedAttribute = property.GetCustomAttribute<DeprecatedInVersionAttribute>();

            yield return new MessagePropertyInfo(
                property.Name,
                property.PropertyType,
                addedInVersion,
                deprecatedAttribute?.Version,
                deprecatedAttribute?.Reason,
                deprecatedAttribute?.ReplacedBy
            );
        }
    }

    private static string GetMessageTypeName(Type messageType)
    {
        // Use full name for uniqueness, but could be customized
        return messageType.FullName ?? messageType.Name;
    }

    private static bool IsDefaultValue(object value, Type type)
    {
        if (value == null) return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;

        if (type.IsValueType)
        {
            var defaultValue = Activator.CreateInstance(type);
            return value.Equals(defaultValue);
        }

        return false;
    }
}

/// <summary>
/// Interface for resolving message versions and managing version-related operations.
/// Provides capabilities for version detection, validation, and version information analysis.
/// </summary>
/// <remarks>
/// The IMessageVersionResolver is the core component for version management in HeroMessaging.
/// It discovers message versions through multiple strategies and provides comprehensive
/// version information for migration and compatibility checking.
///
/// Version Resolution Strategies:
/// 1. MessageVersionAttribute: Declarative version on message type
/// 2. IVersionedMessage interface: Version from message instance
/// 3. Default version: Falls back to 1.0.0 if no version specified
///
/// Key Capabilities:
/// - Version detection from types and instances
/// - Property-level version tracking (AddedInVersion, DeprecatedInVersion)
/// - Version validation against target versions
/// - Known version enumeration for migration planning
///
/// <code>
/// // Resolve version from type
/// var resolver = serviceProvider.GetRequiredService&lt;IMessageVersionResolver&gt;();
/// var version = resolver.GetVersion(typeof(OrderCreatedEvent));
/// Console.WriteLine($"Current version: {version}"); // "2.1.0"
///
/// // Get comprehensive version information
/// var versionInfo = resolver.GetVersionInfo(typeof(OrderCreatedEvent));
/// foreach (var property in versionInfo.Properties)
/// {
///     if (property.AddedInVersion.HasValue)
///     {
///         Console.WriteLine($"{property.Name} added in {property.AddedInVersion}");
///     }
///     if (property.IsDeprecated(versionInfo.Version))
///     {
///         Console.WriteLine($"{property.Name} deprecated: {property.DeprecationReason}");
///     }
/// }
///
/// // Validate message against target version
/// var message = new OrderCreatedEvent { CustomerId = "CUST-001", Priority = "High" };
/// var result = resolver.ValidateMessage(message, new MessageVersion(2, 0, 0));
/// if (!result.IsValid)
/// {
///     foreach (var error in result.Errors)
///         Console.WriteLine($"Validation error: {error}");
/// }
/// </code>
///
/// Version Migration Example:
/// <code>
/// [MessageVersion(2, 1, 0)]
/// public class OrderCreatedEvent : IMessage
/// {
///     public string OrderId { get; set; }
///     public string CustomerId { get; set; }
///
///     [AddedInVersion(2, 0, 0)]
///     public decimal TotalAmount { get; set; }
///
///     [AddedInVersion(2, 1, 0)]
///     public string Priority { get; set; }
///
///     [DeprecatedInVersion(2, 0, 0, Reason = "Use TotalAmount instead", ReplacedBy = nameof(TotalAmount))]
///     public decimal Amount { get; set; }
/// }
///
/// // Resolver automatically detects:
/// // - Current version: 2.1.0
/// // - Priority added in 2.1.0 (not available in 2.0.0)
/// // - Amount deprecated in 2.0.0 (use TotalAmount instead)
/// // - TotalAmount added in 2.0.0 (not available in 1.x)
/// </code>
/// </remarks>
public interface IMessageVersionResolver
{
    /// <summary>
    /// Gets the version of a message type by examining attributes and interfaces.
    /// </summary>
    /// <param name="messageType">The message type to get the version for. Must not be null.</param>
    /// <returns>
    /// The MessageVersion for the specified type.
    /// Returns the version from MessageVersionAttribute if present,
    /// otherwise returns 1.0.0 as the default version.
    /// </returns>
    /// <remarks>
    /// Version resolution order:
    /// 1. MessageVersionAttribute on the type
    /// 2. IVersionedMessage.Version from a default instance (if parameterless constructor exists)
    /// 3. Default version 1.0.0
    ///
    /// Results are cached for performance, so subsequent calls for the same type are fast.
    ///
    /// <code>
    /// // Get version from attribute
    /// [MessageVersion(2, 1, 0)]
    /// public class OrderCreatedEvent : IMessage { }
    ///
    /// var version = resolver.GetVersion(typeof(OrderCreatedEvent));
    /// Console.WriteLine(version); // "2.1.0"
    ///
    /// // Get version from interface
    /// public class CustomerUpdatedEvent : IVersionedMessage
    /// {
    ///     public MessageVersion Version =&gt; new(3, 0, 0);
    ///     public string MessageType =&gt; "CustomerUpdated";
    /// }
    ///
    /// var version2 = resolver.GetVersion(typeof(CustomerUpdatedEvent));
    /// Console.WriteLine(version2); // "3.0.0"
    /// </code>
    /// </remarks>
    MessageVersion GetVersion(Type messageType);

    /// <summary>
    /// Gets the version of a message instance, preferring instance-level version over type-level.
    /// </summary>
    /// <param name="message">The message instance to get the version for. Must not be null.</param>
    /// <returns>
    /// The MessageVersion for the specified message instance.
    /// Returns the instance's Version property if it implements IVersionedMessage,
    /// otherwise returns the version of its type.
    /// </returns>
    /// <remarks>
    /// This method is useful when messages might have instance-specific versions,
    /// such as when deserializing messages from different versions.
    ///
    /// Version resolution order:
    /// 1. IVersionedMessage.Version property from the instance
    /// 2. Fall back to GetVersion(message.GetType())
    ///
    /// <code>
    /// // Instance with specific version
    /// var message = new OrderCreatedEvent
    /// {
    ///     Version = new MessageVersion(2, 0, 0),
    ///     OrderId = "ORD-001"
    /// };
    ///
    /// var version = resolver.GetVersion(message);
    /// Console.WriteLine(version); // "2.0.0" (from instance)
    ///
    /// // Type without IVersionedMessage
    /// var simpleMessage = new SimpleCommand();
    /// var version2 = resolver.GetVersion(simpleMessage);
    /// Console.WriteLine(version2); // Version from type or 1.0.0
    /// </code>
    /// </remarks>
    MessageVersion GetVersion(IMessage message);

    /// <summary>
    /// Gets comprehensive version information for a message type, including property-level versioning details.
    /// </summary>
    /// <param name="messageType">The message type to analyze. Must not be null.</param>
    /// <returns>
    /// A MessageVersionInfo object containing:
    /// - Message type and current version
    /// - Property-level version information (AddedInVersion, DeprecatedInVersion)
    /// - Deprecation reasons and replacement information
    /// </returns>
    /// <remarks>
    /// This method provides comprehensive version metadata useful for:
    /// - Version migration planning
    /// - Backward compatibility analysis
    /// - Documentation generation
    /// - Runtime validation
    ///
    /// Results are cached for performance.
    ///
    /// <code>
    /// [MessageVersion(2, 1, 0)]
    /// public class OrderCreatedEvent : IMessage
    /// {
    ///     public string OrderId { get; set; }
    ///
    ///     [AddedInVersion(2, 0, 0)]
    ///     public decimal TotalAmount { get; set; }
    ///
    ///     [DeprecatedInVersion(2, 0, 0, Reason = "Use TotalAmount", ReplacedBy = nameof(TotalAmount))]
    ///     public decimal Amount { get; set; }
    /// }
    ///
    /// var info = resolver.GetVersionInfo(typeof(OrderCreatedEvent));
    /// Console.WriteLine($"Type: {info.TypeName}");
    /// Console.WriteLine($"Version: {info.Version}");
    ///
    /// foreach (var prop in info.Properties)
    /// {
    ///     Console.WriteLine($"Property: {prop.Name}");
    ///     if (prop.AddedInVersion.HasValue)
    ///         Console.WriteLine($"  Added in: {prop.AddedInVersion}");
    ///     if (prop.IsDeprecated(info.Version))
    ///         Console.WriteLine($"  Deprecated: {prop.DeprecationReason}");
    /// }
    /// </code>
    /// </remarks>
    MessageVersionInfo GetVersionInfo(Type messageType);

    /// <summary>
    /// Validates that a message instance conforms to the constraints of a target version.
    /// </summary>
    /// <param name="message">The message instance to validate. Must not be null.</param>
    /// <param name="targetVersion">The target version to validate against.</param>
    /// <returns>
    /// A MessageVersionValidationResult containing:
    /// - IsValid: true if message conforms to target version
    /// - Errors: List of validation errors (e.g., using properties not available in target version)
    /// - Warnings: List of warnings (e.g., using deprecated properties)
    /// </returns>
    /// <remarks>
    /// Validation checks:
    /// - Message version compatibility with target version (same major version)
    /// - Properties added after target version are not set
    /// - Properties deprecated before target version generate warnings
    ///
    /// Use this to ensure messages are compatible before sending to older consumers.
    ///
    /// <code>
    /// var message = new OrderCreatedEvent
    /// {
    ///     OrderId = "ORD-001",
    ///     TotalAmount = 100.00m,
    ///     Priority = "High" // Added in 2.1.0
    /// };
    ///
    /// // Validate against version 2.0.0
    /// var result = resolver.ValidateMessage(message, new MessageVersion(2, 0, 0));
    ///
    /// if (!result.IsValid)
    /// {
    ///     Console.WriteLine("Validation errors:");
    ///     foreach (var error in result.Errors)
    ///         Console.WriteLine($"  - {error}");
    ///     // Output: "Property 'Priority' was added in version 2.1.0 but target version is 2.0.0"
    /// }
    ///
    /// if (result.HasWarnings)
    /// {
    ///     Console.WriteLine("Warnings:");
    ///     foreach (var warning in result.Warnings)
    ///         Console.WriteLine($"  - {warning}");
    /// }
    /// </code>
    /// </remarks>
    MessageVersionValidationResult ValidateMessage(IMessage message, MessageVersion targetVersion);

    /// <summary>
    /// Gets all known versions for a message type.
    /// </summary>
    /// <param name="messageType">The message type to get known versions for. Must not be null.</param>
    /// <returns>
    /// An enumerable of MessageVersion objects representing all known versions of the message type.
    /// In the current implementation, returns only the current version.
    /// Future implementations may return all registered versions from a version registry.
    /// </returns>
    /// <remarks>
    /// This method is designed to support version migration planning by providing
    /// a complete list of all versions that have existed for a message type.
    ///
    /// Current implementation returns only the current version.
    /// Future enhancements may include:
    /// - Version registry tracking all historical versions
    /// - Database or configuration-based version storage
    /// - Discovery of versions from migration converters
    ///
    /// <code>
    /// var versions = resolver.GetKnownVersions(typeof(OrderCreatedEvent));
    /// Console.WriteLine("Known versions:");
    /// foreach (var version in versions)
    /// {
    ///     Console.WriteLine($"  - {version}");
    /// }
    /// // Output: "  - 2.1.0" (current version)
    ///
    /// // Plan migration path
    /// var allVersions = versions.OrderBy(v =&gt; v).ToList();
    /// for (int i = 0; i &lt; allVersions.Count - 1; i++)
    /// {
    ///     Console.WriteLine($"Migration: {allVersions[i]} -&gt; {allVersions[i + 1]}");
    /// }
    /// </code>
    /// </remarks>
    IEnumerable<MessageVersion> GetKnownVersions(Type messageType);
}

/// <summary>
/// Comprehensive information about a message type's versioning, including version metadata and property-level version tracking.
/// </summary>
/// <remarks>
/// MessageVersionInfo aggregates all version-related metadata for a message type,
/// providing a complete picture of version evolution including property additions,
/// deprecations, and replacements.
///
/// Use this class to:
/// - Analyze version compatibility between message types
/// - Generate migration documentation
/// - Validate messages against specific versions
/// - Build tooling for version management
///
/// <code>
/// var resolver = serviceProvider.GetRequiredService&lt;IMessageVersionResolver&gt;();
/// var versionInfo = resolver.GetVersionInfo(typeof(OrderCreatedEvent));
///
/// Console.WriteLine($"Message Type: {versionInfo.TypeName}");
/// Console.WriteLine($"Current Version: {versionInfo.Version}");
/// Console.WriteLine($"Properties: {versionInfo.Properties.Count}");
///
/// // Find properties added in specific version
/// var v2Properties = versionInfo.Properties
///     .Where(p =&gt; p.AddedInVersion?.Major == 2)
///     .ToList();
///
/// // Find deprecated properties
/// var deprecated = versionInfo.Properties
///     .Where(p =&gt; p.IsDeprecated(versionInfo.Version))
///     .ToList();
/// </code>
/// </remarks>
public class MessageVersionInfo(
    Type messageType,
    MessageVersion version,
    string typeName,
    IReadOnlyList<MessagePropertyInfo> properties)
{
    /// <summary>
    /// Gets the message type that this version information describes.
    /// </summary>
    public Type MessageType { get; } = messageType;

    /// <summary>
    /// Gets the current version of the message type.
    /// </summary>
    public MessageVersion Version { get; } = version;

    /// <summary>
    /// Gets the fully-qualified type name of the message.
    /// </summary>
    public string TypeName { get; } = typeName;

    /// <summary>
    /// Gets the collection of property version information for all public properties on the message type.
    /// </summary>
    public IReadOnlyList<MessagePropertyInfo> Properties { get; } = properties;
}

/// <summary>
/// Information about a property in a versioned message, including version tracking and deprecation metadata.
/// </summary>
/// <remarks>
/// MessagePropertyInfo tracks the lifecycle of a message property across versions:
/// - When it was added (AddedInVersion)
/// - When it was deprecated (DeprecatedInVersion)
/// - Why it was deprecated (DeprecationReason)
/// - What replaced it (ReplacedBy)
///
/// This enables version-aware validation and migration logic.
///
/// <code>
/// [MessageVersion(3, 0, 0)]
/// public class OrderCreatedEvent : IMessage
/// {
///     public string OrderId { get; set; } // Available in all versions
///
///     [AddedInVersion(2, 0, 0)]
///     public decimal TotalAmount { get; set; } // Added in v2.0.0
///
///     [DeprecatedInVersion(2, 0, 0, Reason = "Use TotalAmount", ReplacedBy = nameof(TotalAmount))]
///     public decimal Amount { get; set; } // Deprecated in v2.0.0
/// }
///
/// var resolver = serviceProvider.GetRequiredService&lt;IMessageVersionResolver&gt;();
/// var info = resolver.GetVersionInfo(typeof(OrderCreatedEvent));
///
/// foreach (var prop in info.Properties)
/// {
///     Console.WriteLine($"Property: {prop.Name}");
///
///     // Check if available in version 1.5.0
///     if (!prop.IsAvailable(new MessageVersion(1, 5, 0)))
///         Console.WriteLine($"  Not available in 1.5.0 (added in {prop.AddedInVersion})");
///
///     // Check if deprecated in current version
///     if (prop.IsDeprecated(info.Version))
///         Console.WriteLine($"  Deprecated: {prop.DeprecationReason}, use {prop.ReplacedBy} instead");
/// }
/// </code>
/// </remarks>
public class MessagePropertyInfo(
    string name,
    Type propertyType,
    MessageVersion? addedInVersion = null,
    MessageVersion? deprecatedInVersion = null,
    string? deprecationReason = null,
    string? replacedBy = null)
{
    /// <summary>
    /// Gets the name of the property.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets the type of the property.
    /// </summary>
    public Type PropertyType { get; } = propertyType;

    /// <summary>
    /// Gets the version in which this property was added, or null if it was present in the initial version.
    /// </summary>
    public MessageVersion? AddedInVersion { get; } = addedInVersion;

    /// <summary>
    /// Gets the version in which this property was deprecated, or null if it is not deprecated.
    /// </summary>
    public MessageVersion? DeprecatedInVersion { get; } = deprecatedInVersion;

    /// <summary>
    /// Gets the reason why this property was deprecated, or null if not deprecated.
    /// </summary>
    public string? DeprecationReason { get; } = deprecationReason;

    /// <summary>
    /// Gets the name of the property that replaces this one, or null if not replaced.
    /// </summary>
    public string? ReplacedBy { get; } = replacedBy;

    /// <summary>
    /// Checks if this property is deprecated in the specified version.
    /// </summary>
    /// <param name="version">The version to check against.</param>
    /// <returns>True if the property is deprecated in the specified version, false otherwise.</returns>
    /// <remarks>
    /// A property is considered deprecated if DeprecatedInVersion is set and
    /// the specified version is greater than or equal to the deprecation version.
    ///
    /// <code>
    /// var propertyInfo = new MessagePropertyInfo(
    ///     "Amount",
    ///     typeof(decimal),
    ///     deprecatedInVersion: new MessageVersion(2, 0, 0));
    ///
    /// propertyInfo.IsDeprecated(new MessageVersion(1, 9, 0)); // false
    /// propertyInfo.IsDeprecated(new MessageVersion(2, 0, 0)); // true
    /// propertyInfo.IsDeprecated(new MessageVersion(2, 1, 0)); // true
    /// </code>
    /// </remarks>
    public bool IsDeprecated(MessageVersion version) =>
        DeprecatedInVersion.HasValue && version >= DeprecatedInVersion.Value;

    /// <summary>
    /// Checks if this property is available (not yet added) in the specified version.
    /// </summary>
    /// <param name="version">The version to check against.</param>
    /// <returns>True if the property is available in the specified version, false otherwise.</returns>
    /// <remarks>
    /// A property is available if:
    /// - AddedInVersion is null (property existed in initial version), OR
    /// - The specified version is greater than or equal to AddedInVersion
    ///
    /// <code>
    /// var propertyInfo = new MessagePropertyInfo(
    ///     "Priority",
    ///     typeof(string),
    ///     addedInVersion: new MessageVersion(2, 1, 0));
    ///
    /// propertyInfo.IsAvailable(new MessageVersion(2, 0, 0)); // false
    /// propertyInfo.IsAvailable(new MessageVersion(2, 1, 0)); // true
    /// propertyInfo.IsAvailable(new MessageVersion(3, 0, 0)); // true
    /// </code>
    /// </remarks>
    public bool IsAvailable(MessageVersion version) =>
        !AddedInVersion.HasValue || version >= AddedInVersion.Value;
}

/// <summary>
/// Result of message version validation, containing validation status, errors, and warnings.
/// </summary>
/// <remarks>
/// MessageVersionValidationResult provides comprehensive validation feedback when checking
/// if a message conforms to a specific target version. It distinguishes between:
/// - Errors: Validation failures that prevent compatibility (e.g., using properties not yet added)
/// - Warnings: Non-critical issues (e.g., using deprecated properties)
///
/// Use this to:
/// - Validate messages before sending to older consumers
/// - Provide feedback during development about version compatibility
/// - Implement version-aware message routing
/// - Generate migration warnings and errors
///
/// <code>
/// var message = new OrderCreatedEvent
/// {
///     OrderId = "ORD-001",
///     TotalAmount = 100.00m,
///     Priority = "High", // Added in 2.1.0
///     Amount = 100.00m   // Deprecated in 2.0.0
/// };
///
/// var resolver = serviceProvider.GetRequiredService&lt;IMessageVersionResolver&gt;();
/// var result = resolver.ValidateMessage(message, new MessageVersion(2, 0, 0));
///
/// if (!result.IsValid)
/// {
///     Console.WriteLine($"Validation failed with {result.Errors.Count} error(s):");
///     foreach (var error in result.Errors)
///         Console.WriteLine($"  ERROR: {error}");
///     // ERROR: Property 'Priority' was added in version 2.1.0 but target version is 2.0.0
/// }
///
/// if (result.HasWarnings)
/// {
///     Console.WriteLine($"Validation warnings ({result.Warnings.Count}):");
///     foreach (var warning in result.Warnings)
///         Console.WriteLine($"  WARNING: {warning}");
///     // WARNING: Property 'Amount' is deprecated since version 2.0.0: Use TotalAmount instead
/// }
/// </code>
/// </remarks>
public class MessageVersionValidationResult(bool isValid, IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
{
    /// <summary>
    /// Gets a value indicating whether the message is valid for the target version.
    /// True if no errors were found, false if validation errors exist.
    /// </summary>
    public bool IsValid { get; } = isValid;

    /// <summary>
    /// Gets the collection of validation errors that prevent compatibility with the target version.
    /// Empty if validation succeeded.
    /// </summary>
    /// <remarks>
    /// Errors indicate compatibility issues such as:
    /// - Using properties that were added after the target version
    /// - Version incompatibility (different major version)
    /// </remarks>
    public IReadOnlyList<string> Errors { get; } = errors;

    /// <summary>
    /// Gets the collection of validation warnings about potential issues.
    /// Empty if no warnings were generated.
    /// </summary>
    /// <remarks>
    /// Warnings indicate non-critical issues such as:
    /// - Using deprecated properties
    /// - Potential compatibility concerns
    /// </remarks>
    public IReadOnlyList<string> Warnings { get; } = warnings;

    /// <summary>
    /// Gets a value indicating whether validation generated any warnings.
    /// </summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>
    /// Gets a value indicating whether validation generated any errors.
    /// </summary>
    public bool HasErrors => Errors.Count > 0;
}