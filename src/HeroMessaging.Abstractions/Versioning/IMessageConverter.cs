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
public abstract class MessageConverterBase<TMessage> : IMessageConverter<TMessage>
    where TMessage : class, IMessage
{
    /// <summary>
    /// Gets message type.
    /// </summary>
    public Type MessageType => typeof(TMessage);
    /// <summary>
    /// Gets supported version range.
    /// </summary>
    public abstract MessageVersionRange SupportedVersionRange { get; }
    /// <summary>
    /// Executes can convert.
    /// </summary>

    public virtual bool CanConvert(Type messageType, MessageVersion fromVersion, MessageVersion toVersion)
    {
        return messageType == typeof(TMessage) &&
               SupportedVersionRange.Contains(fromVersion) &&
               SupportedVersionRange.Contains(toVersion);
    }
    /// <summary>
    /// Executes convert async.
    /// </summary>

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
    /// <summary>
    /// Gets min version.
    /// </summary>
    public MessageVersion MinVersion { get; }
    /// <summary>
    /// Gets max version.
    /// </summary>
    public MessageVersion MaxVersion { get; }
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageVersionRange"/> class.
    /// </summary>

    public MessageVersionRange(MessageVersion minVersion, MessageVersion maxVersion)
    {
        if (minVersion > maxVersion)
            throw new ArgumentException("Min version cannot be greater than max version");

        MinVersion = minVersion;
        MaxVersion = maxVersion;
    }
    /// <summary>
    /// Initializes a new instance of the <see cref="MessageVersionRange"/> class.
    /// </summary>

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
    /// <summary>
    /// Executes to string.
    /// </summary>

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
    /// <summary>
    /// Gets message type.
    /// </summary>
    public Type MessageType { get; } = messageType;
    /// <summary>
    /// Gets from version.
    /// </summary>
    public MessageVersion FromVersion { get; } = fromVersion;
    /// <summary>
    /// Gets to version.
    /// </summary>
    public MessageVersion ToVersion { get; } = toVersion;
    /// <summary>
    /// Gets steps.
    /// </summary>
    public IReadOnlyList<MessageConversionStep> Steps { get; } = steps.ToList().AsReadOnly();
    /// <summary>
    /// Gets is direct.
    /// </summary>

    public bool IsDirect => Steps.Count == 1;
    /// <summary>
    /// Gets requires multiple steps.
    /// </summary>
    public bool RequiresMultipleSteps => Steps.Count > 1;
}

/// <summary>
/// Represents a single conversion step in a conversion path
/// </summary>
public class MessageConversionStep(MessageVersion fromVersion, MessageVersion toVersion, IMessageConverter converter)
{
    /// <summary>
    /// Gets from version.
    /// </summary>
    public MessageVersion FromVersion { get; } = fromVersion;
    /// <summary>
    /// Gets to version.
    /// </summary>
    public MessageVersion ToVersion { get; } = toVersion;
    /// <summary>
    /// Gets converter.
    /// </summary>
    public IMessageConverter Converter { get; } = converter;
}
