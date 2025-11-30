using System.Collections.Concurrent;
using System.Reflection;
using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Versioning;

/// <summary>
/// Resolves message versions from types and instances
/// </summary>
public class MessageVersionResolver(ILogger<MessageVersionResolver> logger) : IMessageVersionResolver
{
    private readonly ILogger<MessageVersionResolver> _logger = logger;
    private readonly ConcurrentDictionary<Type, MessageVersion> _typeVersionCache = new();
    private readonly ConcurrentDictionary<Type, MessageVersionInfo> _typeInfoCache = new();


    /// <summary>
    /// Gets the version of a message type
    /// </summary>
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
    /// Gets the version of a message instance
    /// </summary>
    public MessageVersion GetVersion(IMessage message)
    {
        if (message is IVersionedMessage versionedMessage)
        {
            return versionedMessage.Version;
        }

        return GetVersion(message.GetType());
    }

    /// <summary>
    /// Gets comprehensive version information for a message type
    /// </summary>
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
    /// Validates that a message conforms to version constraints
    /// </summary>
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
    /// Gets all versions that have been registered for a message type
    /// </summary>
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
/// Interface for resolving message versions
/// </summary>
public interface IMessageVersionResolver
{
    /// <summary>
    /// Gets the version of a message type
    /// </summary>
    MessageVersion GetVersion(Type messageType);

    /// <summary>
    /// Gets the version of a message instance
    /// </summary>
    MessageVersion GetVersion(IMessage message);

    /// <summary>
    /// Gets comprehensive version information for a message type
    /// </summary>
    MessageVersionInfo GetVersionInfo(Type messageType);

    /// <summary>
    /// Validates that a message conforms to version constraints
    /// </summary>
    MessageVersionValidationResult ValidateMessage(IMessage message, MessageVersion targetVersion);

    /// <summary>
    /// Gets all known versions for a message type
    /// </summary>
    IEnumerable<MessageVersion> GetKnownVersions(Type messageType);
}

/// <summary>
/// Comprehensive information about a message type's versioning
/// </summary>
public class MessageVersionInfo(
    Type messageType,
    MessageVersion version,
    string typeName,
    IReadOnlyList<MessagePropertyInfo> properties)
{
    public Type MessageType { get; } = messageType;
    public MessageVersion Version { get; } = version;
    public string TypeName { get; } = typeName;
    public IReadOnlyList<MessagePropertyInfo> Properties { get; } = properties;
}

/// <summary>
/// Information about a property in a versioned message
/// </summary>
public class MessagePropertyInfo(
    string name,
    Type propertyType,
    MessageVersion? addedInVersion = null,
    MessageVersion? deprecatedInVersion = null,
    string? deprecationReason = null,
    string? replacedBy = null)
{
    public string Name { get; } = name;
    public Type PropertyType { get; } = propertyType;
    public MessageVersion? AddedInVersion { get; } = addedInVersion;
    public MessageVersion? DeprecatedInVersion { get; } = deprecatedInVersion;
    public string? DeprecationReason { get; } = deprecationReason;
    public string? ReplacedBy { get; } = replacedBy;

    public bool IsDeprecated(MessageVersion version) =>
        DeprecatedInVersion.HasValue && version >= DeprecatedInVersion.Value;

    public bool IsAvailable(MessageVersion version) =>
        !AddedInVersion.HasValue || version >= AddedInVersion.Value;
}

/// <summary>
/// Result of message version validation
/// </summary>
public class MessageVersionValidationResult(bool isValid, IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
{
    public bool IsValid { get; } = isValid;
    public IReadOnlyList<string> Errors { get; } = errors;
    public IReadOnlyList<string> Warnings { get; } = warnings;

    public bool HasWarnings => Warnings.Count > 0;
    public bool HasErrors => Errors.Count > 0;
}
