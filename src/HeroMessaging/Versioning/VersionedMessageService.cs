using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Versioning;

/// <summary>
/// Service for handling versioned message operations
/// </summary>
public class VersionedMessageService(
    IMessageVersionResolver versionResolver,
    IMessageConverterRegistry converterRegistry,
    ILogger<VersionedMessageService> logger) : IVersionedMessageService
{
    private readonly IMessageVersionResolver _versionResolver = versionResolver ?? throw new ArgumentNullException(nameof(versionResolver));
    private readonly IMessageConverterRegistry _converterRegistry = converterRegistry ?? throw new ArgumentNullException(nameof(converterRegistry));
    private readonly ILogger<VersionedMessageService> _logger = logger;


    /// <summary>
    /// Converts a message to a specific version
    /// </summary>
    public async Task<TMessage> ConvertToVersionAsync<TMessage>(TMessage message, MessageVersion targetVersion, CancellationToken cancellationToken = default)
        where TMessage : class, IMessage
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        var currentVersion = _versionResolver.GetVersion(message);
        
        if (currentVersion == targetVersion)
        {
            _logger.LogDebug("Message {MessageType} is already at target version {Version}",
                typeof(TMessage).Name, targetVersion);
            return message;
        }

        _logger.LogDebug("Converting message {MessageType} from version {FromVersion} to {ToVersion}",
            typeof(TMessage).Name, currentVersion, targetVersion);

        return await ConvertMessageInternal(message, currentVersion, targetVersion, cancellationToken);
    }

    /// <summary>
    /// Ensures a message is compatible with a specific version, converting if necessary
    /// </summary>
    public async Task<TMessage> EnsureCompatibilityAsync<TMessage>(TMessage message, MessageVersion requiredVersion, CancellationToken cancellationToken = default)
        where TMessage : class, IMessage
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        var currentVersion = _versionResolver.GetVersion(message);
        
        // Check if current version is compatible
        if (currentVersion.IsCompatibleWith(requiredVersion))
        {
            _logger.LogDebug("Message {MessageType} version {CurrentVersion} is compatible with required version {RequiredVersion}",
                typeof(TMessage).Name, currentVersion, requiredVersion);
            return message;
        }

        _logger.LogDebug("Message {MessageType} version {CurrentVersion} is not compatible with required version {RequiredVersion}, attempting conversion",
            typeof(TMessage).Name, currentVersion, requiredVersion);

        return await ConvertMessageInternal(message, currentVersion, requiredVersion, cancellationToken);
    }

    /// <summary>
    /// Validates that a message conforms to version constraints
    /// </summary>
    public MessageVersionValidationResult ValidateMessage<TMessage>(TMessage message, MessageVersion targetVersion)
        where TMessage : class, IMessage
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        return _versionResolver.ValidateMessage(message, targetVersion);
    }

    /// <summary>
    /// Gets version information for a message type
    /// </summary>
    public MessageVersionInfo GetVersionInfo<TMessage>() where TMessage : class, IMessage
    {
        return _versionResolver.GetVersionInfo(typeof(TMessage));
    }

    /// <summary>
    /// Gets version information for a message instance
    /// </summary>
    public MessageVersionInfo GetVersionInfo<TMessage>(TMessage message) where TMessage : class, IMessage
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        
        return _versionResolver.GetVersionInfo(message.GetType());
    }

    /// <summary>
    /// Checks if conversion is possible between versions
    /// </summary>
    public bool CanConvert<TMessage>(MessageVersion fromVersion, MessageVersion toVersion) where TMessage : class, IMessage
    {
        return _converterRegistry.CanConvert(typeof(TMessage), fromVersion, toVersion);
    }

    /// <summary>
    /// Finds the conversion path between versions
    /// </summary>
    public MessageConversionPath? FindConversionPath<TMessage>(MessageVersion fromVersion, MessageVersion toVersion) where TMessage : class, IMessage
    {
        return _converterRegistry.FindConversionPath(typeof(TMessage), fromVersion, toVersion);
    }

    private async Task<TMessage> ConvertMessageInternal<TMessage>(TMessage message, MessageVersion fromVersion, MessageVersion toVersion, CancellationToken cancellationToken)
        where TMessage : class, IMessage
    {
        // Find conversion path
        var conversionPath = _converterRegistry.FindConversionPath(typeof(TMessage), fromVersion, toVersion);
        if (conversionPath == null)
        {
            throw new MessageConversionException(
                $"No conversion path found for {typeof(TMessage).Name} from version {fromVersion} to {toVersion}");
        }

        _logger.LogDebug("Found conversion path for {MessageType} with {StepCount} steps",
            typeof(TMessage).Name, conversionPath.Steps.Count);

        // Apply conversion steps
        IMessage currentMessage = message;
        var currentVersion = fromVersion;

        foreach (var step in conversionPath.Steps)
        {
            try
            {
                _logger.LogDebug("Applying conversion step from {FromVersion} to {ToVersion}",
                    step.FromVersion, step.ToVersion);

                currentMessage = await step.Converter.ConvertAsync(currentMessage, currentVersion, step.ToVersion, cancellationToken);
                currentVersion = step.ToVersion;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conversion step failed from {FromVersion} to {ToVersion}",
                    step.FromVersion, step.ToVersion);
                
                throw new MessageConversionException(
                    $"Conversion failed at step {step.FromVersion} -> {step.ToVersion}: {ex.Message}", ex);
            }
        }

        if (currentMessage is not TMessage convertedMessage)
        {
            throw new MessageConversionException(
                $"Conversion resulted in unexpected type {currentMessage.GetType().Name}, expected {typeof(TMessage).Name}");
        }

        _logger.LogDebug("Successfully converted {MessageType} from version {FromVersion} to {ToVersion}",
            typeof(TMessage).Name, fromVersion, toVersion);

        return convertedMessage;
    }
}

/// <summary>
/// Interface for versioned message operations
/// </summary>
public interface IVersionedMessageService
{
    /// <summary>
    /// Converts a message to a specific version
    /// </summary>
    Task<TMessage> ConvertToVersionAsync<TMessage>(TMessage message, MessageVersion targetVersion, CancellationToken cancellationToken = default)
        where TMessage : class, IMessage;

    /// <summary>
    /// Ensures a message is compatible with a specific version, converting if necessary
    /// </summary>
    Task<TMessage> EnsureCompatibilityAsync<TMessage>(TMessage message, MessageVersion requiredVersion, CancellationToken cancellationToken = default)
        where TMessage : class, IMessage;

    /// <summary>
    /// Validates that a message conforms to version constraints
    /// </summary>
    MessageVersionValidationResult ValidateMessage<TMessage>(TMessage message, MessageVersion targetVersion)
        where TMessage : class, IMessage;

    /// <summary>
    /// Gets version information for a message type
    /// </summary>
    MessageVersionInfo GetVersionInfo<TMessage>() where TMessage : class, IMessage;

    /// <summary>
    /// Gets version information for a message instance
    /// </summary>
    MessageVersionInfo GetVersionInfo<TMessage>(TMessage message) where TMessage : class, IMessage;

    /// <summary>
    /// Checks if conversion is possible between versions
    /// </summary>
    bool CanConvert<TMessage>(MessageVersion fromVersion, MessageVersion toVersion) where TMessage : class, IMessage;

    /// <summary>
    /// Finds the conversion path between versions
    /// </summary>
    MessageConversionPath? FindConversionPath<TMessage>(MessageVersion fromVersion, MessageVersion toVersion) where TMessage : class, IMessage;
}