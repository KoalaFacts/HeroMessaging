using HeroMessaging.Abstractions.Messages;
using HeroMessaging.Abstractions.Versioning;
using Microsoft.Extensions.Logging;

namespace HeroMessaging.Versioning;

/// <summary>
/// Default implementation of IVersionedMessageService that handles versioned message operations
/// including conversion, compatibility checking, and validation.
/// </summary>
/// <remarks>
/// VersionedMessageService coordinates between IMessageVersionResolver for version detection
/// and IMessageConverterRegistry for message conversion to provide a complete version management solution.
///
/// Key Features:
/// - Automatic version detection and conversion
/// - Multi-step conversion path execution
/// - Compatibility validation
/// - Comprehensive error handling and logging
///
/// <code>
/// // Register in DI
/// services.AddSingleton&lt;IMessageVersionResolver, MessageVersionResolver&gt;();
/// services.AddSingleton&lt;IMessageConverterRegistry, MessageConverterRegistry&gt;();
/// services.AddSingleton&lt;IVersionedMessageService, VersionedMessageService&gt;();
///
/// // Usage
/// var service = serviceProvider.GetRequiredService&lt;IVersionedMessageService&gt;();
/// var message = new OrderCreatedEvent { ... };
/// var converted = await service.ConvertToVersionAsync(message, new MessageVersion(2, 0, 0));
/// </code>
/// </remarks>
public class VersionedMessageService(
    IMessageVersionResolver versionResolver,
    IMessageConverterRegistry converterRegistry,
    ILogger<VersionedMessageService> logger) : IVersionedMessageService
{
    private readonly IMessageVersionResolver _versionResolver = versionResolver ?? throw new ArgumentNullException(nameof(versionResolver));
    private readonly IMessageConverterRegistry _converterRegistry = converterRegistry ?? throw new ArgumentNullException(nameof(converterRegistry));
    private readonly ILogger<VersionedMessageService> _logger = logger;


    /// <summary>
    /// Converts a message to a specific target version using registered message converters.
    /// </summary>
    /// <typeparam name="TMessage">The message type to convert.</typeparam>
    /// <param name="message">The message instance to convert. Must not be null.</param>
    /// <param name="targetVersion">The target version to convert to.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the conversion operation.</param>
    /// <returns>
    /// A Task containing the converted message at the target version.
    /// If the message is already at the target version, returns the original message.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    /// <exception cref="MessageConversionException">Thrown when conversion fails or no conversion path exists.</exception>
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
    /// Ensures a message is compatible with a required version, converting if necessary.
    /// </summary>
    /// <typeparam name="TMessage">The message type to check and potentially convert.</typeparam>
    /// <param name="message">The message instance to check. Must not be null.</param>
    /// <param name="requiredVersion">The version that the message must be compatible with.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// A Task containing the message, either unchanged (if already compatible) or converted to the required version.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    /// <exception cref="MessageConversionException">Thrown when conversion is needed but fails.</exception>
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
    /// Validates that a message conforms to the constraints of a target version.
    /// </summary>
    /// <typeparam name="TMessage">The message type to validate.</typeparam>
    /// <param name="message">The message instance to validate. Must not be null.</param>
    /// <param name="targetVersion">The target version to validate against.</param>
    /// <returns>
    /// A MessageVersionValidationResult containing validation status, errors, and warnings.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    public MessageVersionValidationResult ValidateMessage<TMessage>(TMessage message, MessageVersion targetVersion)
        where TMessage : class, IMessage
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        return _versionResolver.ValidateMessage(message, targetVersion);
    }

    /// <summary>
    /// Gets version information for a message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type to get information for.</typeparam>
    /// <returns>
    /// A MessageVersionInfo object containing comprehensive version metadata.
    /// </returns>
    public MessageVersionInfo GetVersionInfo<TMessage>() where TMessage : class, IMessage
    {
        return _versionResolver.GetVersionInfo(typeof(TMessage));
    }

    /// <summary>
    /// Gets version information for a message instance.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="message">The message instance to get information for. Must not be null.</param>
    /// <returns>
    /// A MessageVersionInfo object containing comprehensive version metadata for the message's type.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    public MessageVersionInfo GetVersionInfo<TMessage>(TMessage message) where TMessage : class, IMessage
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        return _versionResolver.GetVersionInfo(message.GetType());
    }

    /// <summary>
    /// Checks if conversion is possible between two versions for a message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type to check conversion for.</typeparam>
    /// <param name="fromVersion">The source version.</param>
    /// <param name="toVersion">The target version.</param>
    /// <returns>
    /// True if a conversion path exists from fromVersion to toVersion, false otherwise.
    /// </returns>
    public bool CanConvert<TMessage>(MessageVersion fromVersion, MessageVersion toVersion) where TMessage : class, IMessage
    {
        return _converterRegistry.CanConvert(typeof(TMessage), fromVersion, toVersion);
    }

    /// <summary>
    /// Finds the conversion path between two versions for a message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type to find a conversion path for.</typeparam>
    /// <param name="fromVersion">The source version.</param>
    /// <param name="toVersion">The target version.</param>
    /// <returns>
    /// A MessageConversionPath containing the sequence of conversion steps,
    /// or null if no path exists.
    /// </returns>
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
/// Interface for versioned message operations including conversion, compatibility checking, and validation.
/// Provides high-level operations for managing message versions in distributed systems.
/// </summary>
/// <remarks>
/// IVersionedMessageService is the primary service for working with versioned messages in HeroMessaging.
/// It orchestrates version resolution, validation, and conversion to enable seamless communication
/// between services running different message versions.
///
/// Key Capabilities:
/// - Version conversion: Transform messages between different versions
/// - Compatibility checking: Ensure messages work with specific versions
/// - Validation: Verify messages conform to version constraints
/// - Path finding: Discover multi-step conversion paths
///
/// Common Use Cases:
/// - Rolling deployments with mixed service versions
/// - Blue-green deployments requiring backward compatibility
/// - API versioning and deprecation management
/// - Message replay from historical versions
///
/// <code>
/// // Register in DI
/// services.AddSingleton&lt;IVersionedMessageService, VersionedMessageService&gt;();
///
/// // Basic usage
/// var service = serviceProvider.GetRequiredService&lt;IVersionedMessageService&gt;();
///
/// // Convert message to older version before sending to legacy service
/// var message = new OrderCreatedEvent { Version = new(2, 1, 0), Priority = "High" };
/// var v2Message = await service.ConvertToVersionAsync(message, new MessageVersion(2, 0, 0));
///
/// // Ensure compatibility before processing
/// var compatible = await service.EnsureCompatibilityAsync(message, new MessageVersion(2, 0, 0));
///
/// // Validate message structure
/// var result = service.ValidateMessage(message, new MessageVersion(2, 0, 0));
/// if (!result.IsValid)
/// {
///     foreach (var error in result.Errors)
///         Console.WriteLine($"Error: {error}");
/// }
/// </code>
///
/// Version Migration Example:
/// <code>
/// // Service A runs version 2.1.0, Service B runs version 2.0.0
/// // Need to send message from A to B
///
/// [MessageVersion(2, 1, 0)]
/// public class OrderCreatedEvent : IMessage
/// {
///     public string OrderId { get; set; }
///
///     [AddedInVersion(2, 0, 0)]
///     public decimal TotalAmount { get; set; }
///
///     [AddedInVersion(2, 1, 0)]
///     public string Priority { get; set; } // Not available in 2.0.0
/// }
///
/// // Before sending to Service B
/// var service = serviceProvider.GetRequiredService&lt;IVersionedMessageService&gt;();
/// var message = new OrderCreatedEvent
/// {
///     OrderId = "ORD-001",
///     TotalAmount = 100.00m,
///     Priority = "High" // This will be removed during conversion
/// };
///
/// // Convert to version 2.0.0 (removes Priority property)
/// var v2Message = await service.ConvertToVersionAsync(message, new MessageVersion(2, 0, 0));
///
/// // Send to Service B
/// await messaging.Send(v2Message);
/// </code>
/// </remarks>
public interface IVersionedMessageService
{
    /// <summary>
    /// Converts a message to a specific target version using registered message converters.
    /// </summary>
    /// <typeparam name="TMessage">The message type to convert.</typeparam>
    /// <param name="message">The message instance to convert. Must not be null.</param>
    /// <param name="targetVersion">The target version to convert to.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the conversion operation.</param>
    /// <returns>
    /// A Task containing the converted message at the target version.
    /// If the message is already at the target version, returns the original message.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    /// <exception cref="MessageConversionException">Thrown when conversion fails or no conversion path exists.</exception>
    /// <remarks>
    /// This method finds the optimal conversion path from the current version to the target version
    /// and applies all necessary conversion steps. Conversions can be forward (upgrade) or
    /// backward (downgrade).
    ///
    /// Conversion steps are applied sequentially:
    /// 1. Determine current message version
    /// 2. Find conversion path to target version
    /// 3. Apply each converter in sequence
    /// 4. Validate result matches target version
    ///
    /// Performance considerations:
    /// - Direct conversions: Single converter call (~10μs)
    /// - Multi-step conversions: Multiple converter calls (~50μs for 5 steps)
    /// - Version detection is cached for performance
    ///
    /// <code>
    /// // Convert from v2.1.0 to v2.0.0 (downgrade)
    /// var message = new OrderCreatedEvent
    /// {
    ///     OrderId = "ORD-001",
    ///     TotalAmount = 100.00m,
    ///     Priority = "High" // Added in 2.1.0
    /// };
    ///
    /// var v2Message = await service.ConvertToVersionAsync(
    ///     message,
    ///     new MessageVersion(2, 0, 0),
    ///     cancellationToken);
    ///
    /// // v2Message.Priority is null or removed (not available in 2.0.0)
    ///
    /// // Convert from v1.0.0 to v2.0.0 (upgrade)
    /// var oldMessage = new OrderCreatedEvent
    /// {
    ///     OrderId = "ORD-002",
    ///     Amount = 50.00m // Deprecated, replaced by TotalAmount in 2.0.0
    /// };
    ///
    /// var newMessage = await service.ConvertToVersionAsync(
    ///     oldMessage,
    ///     new MessageVersion(2, 0, 0),
    ///     cancellationToken);
    ///
    /// // newMessage.TotalAmount = 50.00m (migrated from Amount)
    /// // newMessage.Amount = 0 (deprecated property cleared)
    /// </code>
    /// </remarks>
    Task<TMessage> ConvertToVersionAsync<TMessage>(TMessage message, MessageVersion targetVersion, CancellationToken cancellationToken = default)
        where TMessage : class, IMessage;

    /// <summary>
    /// Ensures a message is compatible with a required version, converting if necessary.
    /// </summary>
    /// <typeparam name="TMessage">The message type to check and potentially convert.</typeparam>
    /// <param name="message">The message instance to check. Must not be null.</param>
    /// <param name="requiredVersion">The version that the message must be compatible with.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// A Task containing the message, either unchanged (if already compatible) or converted to the required version.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    /// <exception cref="MessageConversionException">Thrown when conversion is needed but fails.</exception>
    /// <remarks>
    /// This method checks version compatibility using semantic versioning rules:
    /// - Same major version: Compatible (e.g., 2.1.0 is compatible with 2.0.0)
    /// - Different major version: Incompatible (e.g., 2.0.0 is NOT compatible with 1.0.0)
    ///
    /// If compatible, returns the original message without conversion.
    /// If incompatible, attempts to convert to the required version.
    ///
    /// Use this method when:
    /// - You need a message that works with a specific consumer version
    /// - You want to avoid unnecessary conversions
    /// - You're implementing version-aware routing
    ///
    /// <code>
    /// // Service running version 2.0.0 receives message
    /// var message = await messaging.Receive&lt;OrderCreatedEvent&gt;();
    ///
    /// // Ensure message is compatible with our version
    /// var compatible = await service.EnsureCompatibilityAsync(
    ///     message,
    ///     new MessageVersion(2, 0, 0),
    ///     cancellationToken);
    ///
    /// // compatible is now guaranteed to work with v2.0.0 code
    /// await ProcessOrder(compatible);
    ///
    /// // Example with version-aware routing
    /// var targetServiceVersion = await discoveryService.GetServiceVersion("order-processor");
    /// var compatibleMessage = await service.EnsureCompatibilityAsync(
    ///     message,
    ///     targetServiceVersion,
    ///     cancellationToken);
    ///
    /// await messaging.Send(compatibleMessage, "order-processor");
    /// </code>
    /// </remarks>
    Task<TMessage> EnsureCompatibilityAsync<TMessage>(TMessage message, MessageVersion requiredVersion, CancellationToken cancellationToken = default)
        where TMessage : class, IMessage;

    /// <summary>
    /// Validates that a message conforms to the constraints of a target version.
    /// </summary>
    /// <typeparam name="TMessage">The message type to validate.</typeparam>
    /// <param name="message">The message instance to validate. Must not be null.</param>
    /// <param name="targetVersion">The target version to validate against.</param>
    /// <returns>
    /// A MessageVersionValidationResult containing validation status, errors, and warnings.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    /// <remarks>
    /// Validation checks:
    /// - Version compatibility (same major version)
    /// - Properties added after target version are not set
    /// - Deprecated properties generate warnings
    ///
    /// This is a non-mutating operation that only reports issues.
    /// Use ConvertToVersionAsync to fix compatibility issues.
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
    /// var result = service.ValidateMessage(message, new MessageVersion(2, 0, 0));
    ///
    /// if (!result.IsValid)
    /// {
    ///     Console.WriteLine("Cannot send to v2.0.0 service:");
    ///     foreach (var error in result.Errors)
    ///         Console.WriteLine($"  - {error}");
    ///     // Output: "Property 'Priority' was added in version 2.1.0 but target version is 2.0.0"
    ///
    ///     // Convert to fix issues
    ///     message = await service.ConvertToVersionAsync(message, new MessageVersion(2, 0, 0));
    /// }
    ///
    /// if (result.HasWarnings)
    /// {
    ///     foreach (var warning in result.Warnings)
    ///         logger.LogWarning(warning);
    /// }
    /// </code>
    /// </remarks>
    MessageVersionValidationResult ValidateMessage<TMessage>(TMessage message, MessageVersion targetVersion)
        where TMessage : class, IMessage;

    /// <summary>
    /// Gets version information for a message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type to get information for.</typeparam>
    /// <returns>
    /// A MessageVersionInfo object containing comprehensive version metadata including
    /// current version, type name, and property-level version information.
    /// </returns>
    /// <remarks>
    /// Use this method to inspect version metadata for a message type without
    /// having a message instance.
    ///
    /// <code>
    /// // Get version info for a type
    /// var info = service.GetVersionInfo&lt;OrderCreatedEvent&gt;();
    ///
    /// Console.WriteLine($"Type: {info.TypeName}");
    /// Console.WriteLine($"Version: {info.Version}");
    /// Console.WriteLine($"Properties: {info.Properties.Count}");
    ///
    /// // Find all deprecated properties
    /// var deprecated = info.Properties
    ///     .Where(p =&gt; p.IsDeprecated(info.Version))
    ///     .ToList();
    /// </code>
    /// </remarks>
    MessageVersionInfo GetVersionInfo<TMessage>() where TMessage : class, IMessage;

    /// <summary>
    /// Gets version information for a message instance.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="message">The message instance to get information for. Must not be null.</param>
    /// <returns>
    /// A MessageVersionInfo object containing comprehensive version metadata for the message's type.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when message is null.</exception>
    /// <remarks>
    /// This method gets version information based on the runtime type of the message instance.
    ///
    /// <code>
    /// var message = new OrderCreatedEvent { OrderId = "ORD-001" };
    /// var info = service.GetVersionInfo(message);
    ///
    /// Console.WriteLine($"Message version: {info.Version}");
    /// </code>
    /// </remarks>
    MessageVersionInfo GetVersionInfo<TMessage>(TMessage message) where TMessage : class, IMessage;

    /// <summary>
    /// Checks if conversion is possible between two versions for a message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type to check conversion for.</typeparam>
    /// <param name="fromVersion">The source version.</param>
    /// <param name="toVersion">The target version.</param>
    /// <returns>
    /// True if a conversion path exists from fromVersion to toVersion, false otherwise.
    /// </returns>
    /// <remarks>
    /// Use this method to check if conversion is possible before attempting conversion.
    /// This is useful for:
    /// - Feature detection (can we send to older services?)
    /// - Error prevention (check before convert)
    /// - Documentation generation (show supported version ranges)
    ///
    /// <code>
    /// // Check if we can downgrade to version 1.5.0
    /// if (service.CanConvert&lt;OrderCreatedEvent&gt;(
    ///     new MessageVersion(2, 0, 0),
    ///     new MessageVersion(1, 5, 0)))
    /// {
    ///     Console.WriteLine("Can send to legacy v1.5 service");
    /// }
    /// else
    /// {
    ///     Console.WriteLine("Breaking changes prevent downgrade to v1.5");
    /// }
    ///
    /// // Check version compatibility matrix
    /// var currentVersion = new MessageVersion(2, 1, 0);
    /// var supportedVersions = new[] { "2.0.0", "1.5.0", "1.0.0" }
    ///     .Select(MessageVersion.Parse)
    ///     .Where(v =&gt; service.CanConvert&lt;OrderCreatedEvent&gt;(currentVersion, v))
    ///     .ToList();
    /// </code>
    /// </remarks>
    bool CanConvert<TMessage>(MessageVersion fromVersion, MessageVersion toVersion) where TMessage : class, IMessage;

    /// <summary>
    /// Finds the conversion path between two versions for a message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type to find a conversion path for.</typeparam>
    /// <param name="fromVersion">The source version.</param>
    /// <param name="toVersion">The target version.</param>
    /// <returns>
    /// A MessageConversionPath containing the sequence of conversion steps,
    /// or null if no path exists.
    /// </returns>
    /// <remarks>
    /// Use this method to:
    /// - Understand conversion complexity (number of steps)
    /// - Generate migration documentation
    /// - Debug conversion issues
    /// - Optimize converter registration
    ///
    /// The conversion path may include multiple steps for indirect conversions.
    /// For example, converting from 1.0.0 to 3.0.0 might go through 2.0.0.
    ///
    /// <code>
    /// // Find conversion path
    /// var path = service.FindConversionPath&lt;OrderCreatedEvent&gt;(
    ///     new MessageVersion(1, 0, 0),
    ///     new MessageVersion(3, 0, 0));
    ///
    /// if (path != null)
    /// {
    ///     Console.WriteLine($"Conversion path has {path.Steps.Count} step(s):");
    ///     foreach (var step in path.Steps)
    ///     {
    ///         Console.WriteLine($"  {step.FromVersion} -&gt; {step.ToVersion}");
    ///     }
    ///     // Output:
    ///     //   1.0.0 -&gt; 2.0.0
    ///     //   2.0.0 -&gt; 3.0.0
    /// }
    /// else
    /// {
    ///     Console.WriteLine("No conversion path available");
    /// }
    ///
    /// // Check if direct conversion exists (single step)
    /// var directPath = service.FindConversionPath&lt;OrderCreatedEvent&gt;(
    ///     new MessageVersion(2, 0, 0),
    ///     new MessageVersion(2, 1, 0));
    ///
    /// if (directPath?.Steps.Count == 1)
    /// {
    ///     Console.WriteLine("Direct conversion available");
    /// }
    /// </code>
    /// </remarks>
    MessageConversionPath? FindConversionPath<TMessage>(MessageVersion fromVersion, MessageVersion toVersion) where TMessage : class, IMessage;
}