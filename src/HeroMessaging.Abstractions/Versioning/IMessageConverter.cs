using HeroMessaging.Abstractions.Messages;

namespace HeroMessaging.Abstractions.Versioning;

/// <summary>
/// Interface for converting messages between different versions
/// </summary>
public interface IMessageConverter
{
    /// <summary>
    /// Determines if this converter can convert between the specified versions
    /// </summary>
    bool CanConvert(Type messageType, MessageVersion fromVersion, MessageVersion toVersion);

    /// <summary>
    /// Converts a message from one version to another
    /// </summary>
    Task<IMessage> ConvertAsync(IMessage message, MessageVersion fromVersion, MessageVersion toVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the supported message type
    /// </summary>
    Type MessageType { get; }

    /// <summary>
    /// Gets the version range this converter supports
    /// </summary>
    MessageVersionRange SupportedVersionRange { get; }
}

/// <summary>
/// Generic interface for type-safe message conversion
/// </summary>
/// <typeparam name="TMessage">The message type</typeparam>
public interface IMessageConverter<TMessage> : IMessageConverter
    where TMessage : class, IMessage
{
    /// <summary>
    /// Converts a message from one version to another with type safety
    /// </summary>
    Task<TMessage> ConvertAsync(TMessage message, MessageVersion fromVersion, MessageVersion toVersion, CancellationToken cancellationToken = default);
}

/// <summary>
/// Base class for implementing message converters
/// </summary>
/// <typeparam name="TMessage">The message type</typeparam>
public abstract class MessageConverter<TMessage> : IMessageConverter<TMessage>
    where TMessage : class, IMessage
{
    public Type MessageType => typeof(TMessage);
    public abstract MessageVersionRange SupportedVersionRange { get; }

    public virtual bool CanConvert(Type messageType, MessageVersion fromVersion, MessageVersion toVersion)
    {
        return messageType == typeof(TMessage) &&
               SupportedVersionRange.Contains(fromVersion) &&
               SupportedVersionRange.Contains(toVersion);
    }

    public abstract Task<TMessage> ConvertAsync(TMessage message, MessageVersion fromVersion, MessageVersion toVersion, CancellationToken cancellationToken = default);

    async Task<IMessage> IMessageConverter.ConvertAsync(IMessage message, MessageVersion fromVersion, MessageVersion toVersion, CancellationToken cancellationToken)
    {
        if (message is not TMessage typedMessage)
            throw new ArgumentException($"Message must be of type {typeof(TMessage).Name}", nameof(message));

        return await ConvertAsync(typedMessage, fromVersion, toVersion, cancellationToken);
    }
}

/// <summary>
/// Represents a range of message versions
/// </summary>
public readonly record struct MessageVersionRange
{
    public MessageVersion MinVersion { get; }
    public MessageVersion MaxVersion { get; }

    public MessageVersionRange(MessageVersion minVersion, MessageVersion maxVersion)
    {
        if (minVersion > maxVersion)
            throw new ArgumentException("Min version cannot be greater than max version");

        MinVersion = minVersion;
        MaxVersion = maxVersion;
    }

    public MessageVersionRange(MessageVersion singleVersion)
    {
        MinVersion = MaxVersion = singleVersion;
    }

    /// <summary>
    /// Checks if the range contains the specified version
    /// </summary>
    public bool Contains(MessageVersion version)
    {
        return version >= MinVersion && version <= MaxVersion;
    }

    /// <summary>
    /// Checks if this range overlaps with another range
    /// </summary>
    public bool Overlaps(MessageVersionRange other)
    {
        return MinVersion <= other.MaxVersion && MaxVersion >= other.MinVersion;
    }

    public override string ToString() =>
        MinVersion == MaxVersion ? MinVersion.ToString() : $"{MinVersion}-{MaxVersion}";
}

/// <summary>
/// Registry for message converters
/// </summary>
public interface IMessageConverterRegistry
{
    /// <summary>
    /// Registers a message converter
    /// </summary>
    void RegisterConverter<TMessage>(IMessageConverter<TMessage> converter) where TMessage : class, IMessage;

    /// <summary>
    /// Gets a converter for the specified message type and version range
    /// </summary>
    IMessageConverter<TMessage>? GetConverter<TMessage>(MessageVersion fromVersion, MessageVersion toVersion) where TMessage : class, IMessage;

    /// <summary>
    /// Gets a converter for the specified message type and version range
    /// </summary>
    IMessageConverter? GetConverter(Type messageType, MessageVersion fromVersion, MessageVersion toVersion);

    /// <summary>
    /// Gets all registered converters for a message type
    /// </summary>
    IEnumerable<IMessageConverter> GetConverters(Type messageType);

    /// <summary>
    /// Checks if conversion is possible between versions
    /// </summary>
    bool CanConvert(Type messageType, MessageVersion fromVersion, MessageVersion toVersion);

    /// <summary>
    /// Finds the conversion path between two versions (may involve multiple steps)
    /// </summary>
    MessageConversionPath? FindConversionPath(Type messageType, MessageVersion fromVersion, MessageVersion toVersion);
}

/// <summary>
/// Represents a path for converting between message versions
/// </summary>
public class MessageConversionPath(Type messageType, MessageVersion fromVersion, MessageVersion toVersion, IEnumerable<MessageConversionStep> steps)
{
    public Type MessageType { get; } = messageType;
    public MessageVersion FromVersion { get; } = fromVersion;
    public MessageVersion ToVersion { get; } = toVersion;
    public IReadOnlyList<MessageConversionStep> Steps { get; } = steps.ToList().AsReadOnly();

    public bool IsDirect => Steps.Count == 1;
    public bool RequiresMultipleSteps => Steps.Count > 1;
}

/// <summary>
/// Represents a single conversion step in a conversion path
/// </summary>
public class MessageConversionStep(MessageVersion fromVersion, MessageVersion toVersion, IMessageConverter converter)
{
    public MessageVersion FromVersion { get; } = fromVersion;
    public MessageVersion ToVersion { get; } = toVersion;
    public IMessageConverter Converter { get; } = converter;
}